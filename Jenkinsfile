pipeline {
    agent any

    options {
        buildDiscarder(logRotator(daysToKeepStr: '3', numToKeepStr: '10'))
    }

    triggers {
        cron('30 18 * * *') // midnight IST
    }

    environment {

        // QA
        COMMONDB_QA     = credentials('CONNECTIONSTRINGS__COMMONDB_QA')
        COMMUNITYDB_QA  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_QA')
        INVENTORYDB_QA  = credentials('CONNECTIONSTRINGS__INVENTORYDB_QA')
        PAYROLLDB_QA    = credentials('CONNECTIONSTRINGS__PAYROLLDB_QA')
        PURCHASEDB_QA   = credentials('CONNECTIONSTRINGS__PURCHASEDB_QA')
        SALESDB_QA      = credentials('CONNECTIONSTRINGS__SALESDB_QA')

        // PROD
        COMMONDB_PROD     = credentials('CONNECTIONSTRINGS__COMMONDB_PROD')
        COMMUNITYDB_PROD  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_PROD')
        INVENTORYDB_PROD  = credentials('CONNECTIONSTRINGS__INVENTORYDB_PROD')
        PAYROLLDB_PROD    = credentials('CONNECTIONSTRINGS__PAYROLLDB_PROD')
        PURCHASEDB_PROD   = credentials('CONNECTIONSTRINGS__PURCHASEDB_PROD')
        SALESDB_PROD      = credentials('CONNECTIONSTRINGS__SALESDB_PROD')

        // TEST
        COMMONDB_TEST     = credentials('CONNECTIONSTRINGS__COMMONDB_TEST')
        COMMUNITYDB_TEST  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_TEST')
        INVENTORYDB_TEST  = credentials('CONNECTIONSTRINGS__INVENTORYDB_TEST')
        PAYROLLDB_TEST    = credentials('CONNECTIONSTRINGS__PAYROLLDB_TEST')
        PURCHASEDB_TEST   = credentials('CONNECTIONSTRINGS__PURCHASEDB_TEST')
        SALESDB_TEST      = credentials('CONNECTIONSTRINGS__SALESDB_TEST')

        DO_HOST       = credentials('DO_HOST')
        DO_USER       = credentials('DO_USER')
        DO_SSH_KEY_ID = 'DO_SSH_KEY'

        PATH = "$HOME/.dotnet:$PATH"
    }

    stages {

        stage('Checkout Repo') {
            steps {
                checkout([
                    $class: 'GitSCM',
                    branches: [[name: '*/main']],
                    userRemoteConfigs: [[
                        url: 'https://github.com/b2atech/db-tula.git',
                        credentialsId: 'bharat-mane-git-personal-token'
                    ]]
                ])
            }
        }

        stage('Setup .NET') {
            steps {
                sh '''
                    wget https://dot.net/v1/dotnet-install.sh
                    chmod +x dotnet-install.sh
                    ./dotnet-install.sh --channel 9.0
                '''
            }
        }

        stage('Restore Dependencies') {
            steps {
                sh '''
                    dotnet restore src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj
                '''
            }
        }

        stage('Build CLI Tool') {
            steps {
                sh '''
                    dotnet build src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release
                '''
            }
        }

        stage('QA vs PROD Comparison') {
            steps {
                sh '''
                    mkdir -p gh-pages/qa-vs-prod

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$COMMONDB_QA" --target "$COMMONDB_PROD" \
                    --title "Common Schema Comparison (QA vs PROD)" \
                    --out "gh-pages/qa-vs-prod/common.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$COMMUNITYDB_QA" --target "$COMMUNITYDB_PROD" \
                    --title "Community Schema Comparison (QA vs PROD)" \
                    --out "gh-pages/qa-vs-prod/community.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$INVENTORYDB_QA" --target "$INVENTORYDB_PROD" \
                    --title "Inventory Schema Comparison (QA vs PROD)" \
                    --out "gh-pages/qa-vs-prod/inventory.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$PAYROLLDB_QA" --target "$PAYROLLDB_PROD" \
                    --title "Payroll Schema Comparison (QA vs PROD)" \
                    --out "gh-pages/qa-vs-prod/payroll.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$PURCHASEDB_QA" --target "$PURCHASEDB_PROD" \
                    --title "Purchase Schema Comparison (QA vs PROD)" \
                    --out "gh-pages/qa-vs-prod/purchase.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$SALESDB_QA" --target "$SALESDB_PROD" \
                    --title "Sales Schema Comparison (QA vs PROD)" \
                    --out "gh-pages/qa-vs-prod/sales.html"
                '''
            }
        }

        stage('QA vs TEST Comparison') {
            steps {
                sh '''
                    mkdir -p gh-pages/qa-vs-test

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$COMMONDB_QA" --target "$COMMONDB_TEST" \
                    --title "Common Schema Comparison (QA vs TEST)" \
                    --out "gh-pages/qa-vs-test/common.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$COMMUNITYDB_QA" --target "$COMMUNITYDB_TEST" \
                    --title "Community Schema Comparison (QA vs TEST)" \
                    --out "gh-pages/qa-vs-test/community.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$INVENTORYDB_QA" --target "$INVENTORYDB_TEST" \
                    --title "Inventory Schema Comparison (QA vs TEST)" \
                    --out "gh-pages/qa-vs-test/inventory.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$PAYROLLDB_QA" --target "$PAYROLLDB_TEST" \
                    --title "Payroll Schema Comparison (QA vs TEST)" \
                    --out "gh-pages/qa-vs-test/payroll.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$PURCHASEDB_QA" --target "$PURCHASEDB_TEST" \
                    --title "Purchase Schema Comparison (QA vs TEST)" \
                    --out "gh-pages/qa-vs-test/purchase.html"

                    dotnet run --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj --configuration Release -- \
                    --source "$SALESDB_QA" --target "$SALESDB_TEST" \
                    --title "Sales Schema Comparison (QA vs TEST)" \
                    --out "gh-pages/qa-vs-test/sales.html"
                '''
            }
        }

        stage('Deploy Reports to OVH') {
            steps {
                sshagent (credentials: [env.DO_SSH_KEY_ID]) {
                    sh '''
                        scp -o StrictHostKeyChecking=no -r gh-pages/* \
                        ${DO_USER}@${DO_HOST}:/var/www/dbtula-site
                    '''
                }
            }
        }

    }

    post {
        always {
            cleanWs()
        }

        success {
            echo "Schema comparison completed successfully!"
        }

        failure {
            echo "Schema comparison failed!"
        }
    }
}
