using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace b2a.db_tula
{
    public partial class MainWindow : Window
    {
        private List<SavedComparison> savedComparisons;
        private DatabaseConnection _targetConnection; // Add a field for the target connection

        public MainWindow()
        {
            InitializeComponent();
            LoadComparisons();
            
        }

        private void LoadComparisons()
        {
            savedComparisons = ComparisonManager.LoadComparisons();
            UpdateComparisonComboboxes();
        }

        private void SaveComparisons()
        {
            ComparisonManager.SaveComparisons(savedComparisons);
            UpdateComparisonComboboxes();
        }

        private void UpdateComparisonComboboxes()
        {
            ComparisonComboBox.ItemsSource = savedComparisons.Select(c => c.Name).ToList();
        }

        private void AddComparisonButton_Click(object sender, RoutedEventArgs e)
        {
            var inputWindow = new AddNewComparer();
            if (inputWindow.ShowDialog() == true)
            {
                savedComparisons.Add(new SavedComparison
                {
                    Name = inputWindow.ComparisonName,
                    SourceConnectionString = inputWindow.SourceConnectionString,
                    TargetConnectionString = inputWindow.TargetConnectionString
                });
                SaveComparisons();
            }
        }

        private async void CompareSchemasButton_Click(object sender, RoutedEventArgs e)
        {
            ConsoleOutputTextBox.Clear();  // Clear previous logs

            var selectedComparison = savedComparisons.First(c => c.Name == ComparisonComboBox.SelectedItem.ToString());

            string sourceConnectionString = selectedComparison.SourceConnectionString;
            string targetConnectionString = EncryptionHelper.DecryptString(selectedComparison.TargetConnectionString).Replace("&amp", "&");

            var sourceConnection = new DatabaseConnection(sourceConnectionString);
            var targetConnection = new DatabaseConnection(targetConnectionString);

            var sourceSchemaFetcher = new SchemaFetcher(sourceConnection, LogToConsole);
            var targetSchemaFetcher = new SchemaFetcher(targetConnection, LogToConsole);
            var schemaComparer = new SchemaComparer();

            // Reset progress bars and labels
            ProgressBarTable.Value = 0;
            ProgressBarProcedures.Value = 0;
            ProgressBarFunctions.Value = 0;

            TableProgressLabel.Text = "Tables: 0/0";
            FunctionProgressLabel.Text = "Functions: 0/0";
            ProcedureProgressLabel.Text = "Procedures: 0/0";

            // ================= Fetch Tables ====================
            LogToConsole("Fetching Tables...");
            var sourceTables = await Task.Run(() => sourceSchemaFetcher.GetTables());
            var targetTables = await Task.Run(() => targetSchemaFetcher.GetTables());

            int totalTables = schemaComparer.GetUniqueCount(sourceTables, targetTables, "table_name");
            ProgressBarTable.Maximum = totalTables;

            // Update label
            TableProgressLabel.Text = $"Tables: 0/{totalTables}";

            // ================= Fetch Functions ====================
            LogToConsole("Fetching Functions...");
            var sourceFunctions = await Task.Run(() => sourceSchemaFetcher.GetFunctions());
            var targetFunctions = await Task.Run(() => targetSchemaFetcher.GetFunctions());

            int totalFunctions = schemaComparer.GetUniqueCount(sourceFunctions, targetFunctions, "routine_name");
            ProgressBarFunctions.Maximum = totalFunctions;

            // Update label
            FunctionProgressLabel.Text = $"Functions: 0/{totalFunctions}";

            // ================= Fetch Procedures ====================
            LogToConsole("Fetching Procedures...");
            var sourceProcedures = await Task.Run(() => sourceSchemaFetcher.GetProcedures());
            var targetProcedures = await Task.Run(() => targetSchemaFetcher.GetProcedures());

            int totalProcedures = schemaComparer.GetUniqueCount(sourceProcedures, targetProcedures, "routine_name");
            ProgressBarProcedures.Maximum = totalProcedures;

            // Update label
            ProcedureProgressLabel.Text = $"Procedures: 0/{totalProcedures}";

            // ================= Compare Tables ====================
            LogToConsole("Comparing Tables...");

            var tableDifferences = await Task.Run(() =>
               schemaComparer.CompareTables(sourceTables, targetTables, sourceConnectionString, targetConnectionString, (currentTable, total) =>
               {
                   Dispatcher.Invoke(() =>
                   {
                       ProgressBarTable.Value = currentTable;
                       TableProgressLabel.Text = $"Tables: {currentTable}/{total}";
                   });
               })
            );

            // ================= Compare Functions ====================
            LogToConsole("Comparing Functions...");

            var functionDifferences = await Task.Run(() =>
                schemaComparer.CompareFunctions(sourceFunctions, targetFunctions, sourceSchemaFetcher, targetSchemaFetcher, (currentFunction, total) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBarFunctions.Value = currentFunction;
                        FunctionProgressLabel.Text = $"Functions: {currentFunction}/{total}";
                    });
                })
            );

            // ================= Compare Procedures ====================
            LogToConsole("Comparing Procedures...");

            var procedureDifferences = await Task.Run(() =>
                schemaComparer.CompareProcedures(sourceProcedures, targetProcedures, sourceSchemaFetcher, targetSchemaFetcher, (currentProcedure, total) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBarProcedures.Value = currentProcedure;
                        ProcedureProgressLabel.Text = $"Procedures: {currentProcedure}/{total}";
                    });
                })
            );

            Dispatcher.Invoke(() => LogToConsole("Procedure comparison completed."));

            // ================= Combine Results ====================
            var comparisonData = new List<ComparisonResult>();
            comparisonData.AddRange(tableDifferences);
            comparisonData.AddRange(functionDifferences);
            comparisonData.AddRange(procedureDifferences);

            Dispatcher.Invoke(() => TableComparisonGrid.ItemsSource = comparisonData);

            // Complete progress
            //await UpdateProgress(totalItems, totalItems);
            LogToConsole("Comparison process completed.");
        }

        // Helper method to update the progress bar
        //private Task UpdateProgress(int currentStep, int totalSteps)
        //{
        //    return Dispatcher.InvokeAsync(() =>
        //    {
        //        ProgressBar.Value = (currentStep * 100) / totalSteps;
        //    }).Task;
        //}

       

        private void TableComparisonGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TableComparisonGrid.SelectedItem is ComparisonResult selectedItem)
            {
                var selectedComparison = savedComparisons.First(c => c.Name == ComparisonComboBox.SelectedItem.ToString());

                string sourceConnectionString = selectedComparison.SourceConnectionString;
                string targetConnectionString = EncryptionHelper.DecryptString(selectedComparison.TargetConnectionString).Replace("&amp", "&");

                var sourceConnection = new DatabaseConnection(sourceConnectionString);
                var targetConnection = new DatabaseConnection(targetConnectionString);

                var sourceSchemaFetcher = new SchemaFetcher(sourceConnection, LogToConsole);
                var targetSchemaFetcher = new SchemaFetcher(targetConnection, LogToConsole);

                var detailsData = new List<ColumnComparisonResult>();
                var schemaComparer = new SchemaComparer();

                if (selectedItem.Type == "Table")
                {
                    DetailsGrid.Visibility = Visibility.Visible;
                    CodeDisplayGrid.Visibility = Visibility.Collapsed;
                    detailsData = selectedItem.ColumnComparisonResults;
                    if (detailsData == null)
                        detailsData = new List<ColumnComparisonResult>();
                    //var sourceColumns = sourceSchemaFetcher.GetColumns(selectedItem.SourceName);
                    //var targetColumns = targetSchemaFetcher.GetColumns(selectedItem.DestinationName);
                    ////var columnDifferences = schemaComparer.CompareColumns(sourceColumns, targetColumns);

                    ////foreach (var column in columnDifferences)
                    ////{
                    ////    detailsData.Add(new DetailsResult
                    ////    {
                    ////        SourceName = column.SourceName,
                    ////        SourceType = column.SourceType,
                    ////        SourceLength = column.SourceLength,
                    ////        DestinationName = column.DestinationName,
                    ////        DestinationType = column.DestinationType,
                    ////        DestinationLength = column.DestinationLength,
                    ////        Comparison = column.Comparison
                    ////    });
                    ////}

                    var sourcePrimaryKeys = sourceSchemaFetcher.GetPrimaryKeys(selectedItem.SourceName);
                    var targetPrimaryKeys = targetSchemaFetcher.GetPrimaryKeys(selectedItem.DestinationName);
                    var primaryKeyDifferences = schemaComparer.ComparePrimaryKeys(sourcePrimaryKeys, targetPrimaryKeys);

                    foreach (var key in primaryKeyDifferences)
                    {
                        detailsData.Add(new ColumnComparisonResult
                        {
                            SourceName = key.SourceName,
                            SourceType = "PrimaryKey",
                            DestinationName = key.DestinationName,
                            DestinationType = "PrimaryKey",
                            Comparison = key.Comparison
                        });
                    }

                    var sourceForeignKeys = sourceSchemaFetcher.GetForeignKeys(selectedItem.SourceName);
                    var targetForeignKeys = targetSchemaFetcher.GetForeignKeys(selectedItem.DestinationName);
                    var foreignKeyDifferences = schemaComparer.CompareForeignKeys(sourceForeignKeys, targetForeignKeys);

                    foreach (var key in foreignKeyDifferences)
                    {
                        detailsData.Add(new ColumnComparisonResult
                        {
                            SourceName = key.SourceName,
                            SourceType = "ForeignKey",
                            DestinationName = key.DestinationName,
                            DestinationType = "ForeignKey",
                            Comparison = key.Comparison
                        });
                    }
                }
                else if (selectedItem.Type == "Function" || selectedItem.Type == "Procedure")
                {
                    DetailsGrid.Visibility = Visibility.Collapsed;
                    CodeDisplayGrid.Visibility = Visibility.Visible;

                    //string sourceCode = selectedItem.Type == "Function"
                    //    ? sourceSchemaFetcher.GetFunctionDefinition(selectedItem.SourceName)
                    //    : sourceSchemaFetcher.GetProcedureDefinition(selectedItem.SourceName);

                    //string targetCode = selectedItem.Type == "Function"
                    //    ? targetSchemaFetcher.GetFunctionDefinition(selectedItem.DestinationName)
                    //    : targetSchemaFetcher.GetProcedureDefinition(selectedItem.DestinationName);

                    SourceCodeTextBox.Text = selectedItem.SourceDefinition;
                    TargetCodeTextBox.Text = selectedItem.DestinationDefinition;
                }

                DetailsGrid.ItemsSource = detailsData;
            }
        }
        private void LogToConsole(string message)
        {
            Dispatcher.Invoke(() => {
                ConsoleOutputTextBox.AppendText(message + Environment.NewLine);
                ConsoleOutputTextBox.ScrollToEnd();
            });
        }
        private async void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var comparisonResult = button?.CommandParameter as ComparisonResult;

            if (comparisonResult != null)
            {
                var selectedComparison = savedComparisons.First(c => c.Name == ComparisonComboBox.SelectedItem.ToString());
                string sourceConnectionString = selectedComparison.SourceConnectionString;
                string targetConnectionString = EncryptionHelper.DecryptString(selectedComparison.TargetConnectionString).Replace("&amp", "&");

                var sourceConnection = new DatabaseConnection(sourceConnectionString);
                _targetConnection = new DatabaseConnection(targetConnectionString); // Initialize the target connection

                var schemaSyncer = new SchemaSyncer(sourceConnection, _targetConnection, LogToConsole);

                // If comparison is for a table
                if (comparisonResult.Type == "Table")
                {
                    // Clear console output
                    ConsoleOutputTextBox.Clear();

                    // Fetch table definitions from source and target
                    var sourceTableDefinition = await Task.Run(() => new SchemaFetcher(sourceConnection, LogToConsole).GetTableDefinition(comparisonResult.SourceName));

                    var targetTableDefinition = await Task.Run(() => new SchemaFetcher(_targetConnection, LogToConsole).GetTableDefinition(comparisonResult.DestinationName));

                    // Generate the sync commands (but don't execute yet)
                    var syncCommands = await Task.Run(() => schemaSyncer.GenerateSyncCommands(sourceTableDefinition, targetTableDefinition));

                    // Show the SQL commands in a modal dialog or a text area for the user to review
                    ShowSyncCommandsPreview(syncCommands, comparisonResult);

                    // Finish progress
                }
                else if (comparisonResult.Type == "Function" || comparisonResult.Type == "Procedure")
                {

                    var sourceDefinition = await Task.Run(() => new SchemaFetcher(sourceConnection, LogToConsole).GetFunctionOrProcedureDefinition(comparisonResult.SourceName));

                    var targetDefinition = await Task.Run(() => new SchemaFetcher(_targetConnection, LogToConsole).GetFunctionOrProcedureDefinition(comparisonResult.DestinationName));

                    // Generate the sync commands for functions/procedures
                    var syncCommands = await Task.Run(() => schemaSyncer.GenerateSyncCommandsForFunctionsOrProcedures(sourceDefinition, targetDefinition, comparisonResult.Type));

                    // Show the SQL commands in a modal dialog or a text area for the user to review
                    ShowSyncCommandsPreview(syncCommands, comparisonResult);
                }
            }
        }




        private void ShowSyncCommandsPreview(List<string> syncCommands, ComparisonResult comparisonResult)
        {
            // Display the SQL commands in a new window, modal, or text area
            SyncPreviewWindow previewWindow = new SyncPreviewWindow(syncCommands, comparisonResult);
            previewWindow.ShowDialog(); // This opens a modal window with the commands

            if (previewWindow.DialogResult == true)
            {
                // The user clicked "Confirm", execute the sync
                ExecuteSyncCommands(syncCommands);
            }
            else
            {
                LogToConsole("Sync canceled by user.");
            }
        }
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Check if Ctrl + Alt + E is pressed
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.E)
            {
                // Show your custom dialog box
                OpenSecretDialog();
            }
        }
        private void OpenSecretDialog()
        {
            // Your custom dialog logic
            var secretDialog = new SecretDialog(); // Assuming you have a dialog window called SecretDialog
            secretDialog.ShowDialog();
        }

        private void ExecuteSyncCommands(List<string> syncCommands)
        {
            try
            {
                foreach (var command in syncCommands)
                {
                    _targetConnection.ExecuteCommand(command); // Execute command on the target database
                    LogToConsole($"Executed command: {command}");
                }

                LogToConsole("Sync completed successfully.");
            }
            catch (Exception ex)
            {
                LogToConsole($"Error during sync: {ex.Message}");
            }
        }

    }



    public class DetailsResult
    {
        public string SourceName { get; set; }
        public string SourceType { get; set; }
        public string SourceLength { get; set; }
        public string DestinationName { get; set; }
        public string DestinationType { get; set; }
        public string DestinationLength { get; set; }
        public string Comparison { get; set; }
    }

    public class SavedComparison
    {
        public string Name { get; set; }
        public string SourceConnectionString { get; set; }
        public string TargetConnectionString { get; set; }
    }
}
