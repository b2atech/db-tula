using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace b2a.db_tula
{
    public partial class MainWindow : Window
    {
        private List<SavedComparison> savedComparisons;

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

        private void CompareSchemasButton_Click(object sender, RoutedEventArgs e)
        {
            ConsoleOutputTextBox.Clear();

            var selectedComparison = savedComparisons.First(c => c.Name == ComparisonComboBox.SelectedItem.ToString());

            string sourceConnectionString = selectedComparison.SourceConnectionString;
            string targetConnectionString = EncryptionHelper.DecryptString(selectedComparison.TargetConnectionString).Replace("&amp", "&");

            var sourceConnection = new DatabaseConnection(sourceConnectionString);
            var targetConnection = new DatabaseConnection(targetConnectionString);
            

            var sourceSchemaFetcher = new SchemaFetcher(sourceConnection, LogToConsole);
            var targetSchemaFetcher = new SchemaFetcher(targetConnection, LogToConsole);

            LogToConsole("Comparing Tables...");
            var sourceTables = sourceSchemaFetcher.GetTables();
            var targetTables = targetSchemaFetcher.GetTables();
            var schemaComparer = new SchemaComparer();
            var tableDifferences = schemaComparer.CompareTables(sourceTables, targetTables);
            LogToConsole("Table comparison completed.");

            var functionDifferences = schemaComparer.CompareFunctions(sourceSchemaFetcher.GetFunctions(), targetSchemaFetcher.GetFunctions(), sourceSchemaFetcher, targetSchemaFetcher);
            var procedureDifferences = schemaComparer.CompareProcedures(sourceSchemaFetcher.GetProcedures(), targetSchemaFetcher.GetProcedures(), sourceSchemaFetcher, targetSchemaFetcher);

            var comparisonData = new List<ComparisonResult>();
            comparisonData.AddRange(tableDifferences);
            comparisonData.AddRange(functionDifferences);
            comparisonData.AddRange(procedureDifferences);

            TableComparisonGrid.ItemsSource = comparisonData;
        }

        private void TableComparisonGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TableComparisonGrid.SelectedItem is ComparisonResult selectedItem)
            {
                var selectedComparison = savedComparisons.First(c => c.Name == ComparisonComboBox.SelectedItem.ToString());

                string sourceConnectionString = selectedComparison.SourceConnectionString;
                string targetConnectionString = selectedComparison.TargetConnectionString;

                var sourceConnection = new DatabaseConnection(sourceConnectionString);
                var targetConnection = new DatabaseConnection(targetConnectionString);

                var sourceSchemaFetcher = new SchemaFetcher(sourceConnection, LogToConsole);
                var targetSchemaFetcher = new SchemaFetcher(targetConnection, LogToConsole);

                var detailsData = new List<DetailsResult>();
                var schemaComparer = new SchemaComparer();

                if (selectedItem.Type == "Table")
                {
                    DetailsGrid.Visibility = Visibility.Visible;
                    CodeDisplayGrid.Visibility = Visibility.Collapsed;

                    var sourceColumns = sourceSchemaFetcher.GetColumns(selectedItem.SourceName);
                    var targetColumns = targetSchemaFetcher.GetColumns(selectedItem.DestinationName);
                    var columnDifferences = schemaComparer.CompareColumns(sourceColumns, targetColumns);

                    foreach (var column in columnDifferences)
                    {
                        detailsData.Add(new DetailsResult
                        {
                            SourceName = column.SourceName,
                            SourceType = column.SourceType,
                            SourceLength = column.SourceLength,
                            DestinationName = column.DestinationName,
                            DestinationType = column.DestinationType,
                            DestinationLength = column.DestinationLength,
                            Comparison = column.Comparison
                        });
                    }

                    var sourcePrimaryKeys = sourceSchemaFetcher.GetPrimaryKeys(selectedItem.SourceName);
                    var targetPrimaryKeys = targetSchemaFetcher.GetPrimaryKeys(selectedItem.DestinationName);
                    var primaryKeyDifferences = schemaComparer.ComparePrimaryKeys(sourcePrimaryKeys, targetPrimaryKeys);

                    foreach (var key in primaryKeyDifferences)
                    {
                        detailsData.Add(new DetailsResult
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
                        detailsData.Add(new DetailsResult
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

                    string sourceCode = selectedItem.Type == "Function"
                        ? sourceSchemaFetcher.GetFunctionDefinition(selectedItem.SourceName)
                        : sourceSchemaFetcher.GetProcedureDefinition(selectedItem.SourceName);

                    string targetCode = selectedItem.Type == "Function"
                        ? targetSchemaFetcher.GetFunctionDefinition(selectedItem.DestinationName)
                        : targetSchemaFetcher.GetProcedureDefinition(selectedItem.DestinationName);

                    SourceCodeTextBox.Text = sourceCode;
                    TargetCodeTextBox.Text = targetCode;
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
    }

    public class ComparisonResult
    {
        public string Type { get; set; }
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string Comparison { get; set; }
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
