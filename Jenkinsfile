pipeline {
    agent any

    options {
        buildDiscarder(logRotator(daysToKeepStr: '3', numToKeepStr: '10'))
    }

    triggers {
        cron('30 18 * * *')   // midnight IST
    }

    environment {

        // QA
        QA_DB_HOST     = credentials('QA_DB_HOST')
        QA_DB_PORT     = '25061'
        QA_DB_USER     = credentials('QA_DB_USER')
        QA_DB_PASSWORD = credentials('QA_DB_PASSWORD')

        // PROD
        PROD_DB_HOST     = credentials('PROD_DB_HOST')
        PROD_DB_PORT     = '25061'
        PROD_DB_USER     = credentials('PROD_DB_USER')
        PROD_DB_PASSWORD = credentials('PROD_DB_PASSWORD')

        // TEST
        TEST_DB_HOST     = credentials('TEST_DB_HOST')
        TEST_DB_PORT     = '25061'
        TEST_DB_USER     = credentials('TEST_DB_USER')
        TEST_DB_PASSWORD = credentials('TEST_DB_PASSWORD')

        DO_HOST     = credentials('DO_HOST')
        DO_USER     = credentials('DO_USER')
        DO_PASSWORD = credentials('DO_PASSWORD')

        PATH = "$HOME/.dotnet:$PATH"
    }

    stages {

        stage('Checkout Repo') {
            steps {
                checkout scm
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

        stage('Run Schema Comparison QA vs PROD') {
            steps {
                sh '''
                    set -e
                    set -x

                    services=("common" "community" "inventory" "payroll" "purchase" "sales")

                    mkdir -p gh-pages/qa-vs-prod

                    for service in "${services[@]}"; do

                        echo "Comparing QA → PROD for $service..."

                        SOURCE_CONN="Server=${QA_DB_HOST};Port=${QA_DB_PORT};Database=qa-dhanman-${service};User Id=${QA_DB_USER};Password=${QA_DB_PASSWORD};"

                        TARGET_CONN="Server=${PROD_DB_HOST};Port=${PROD_DB_PORT};Database=prod-dhanman-${service};User Id=${PROD_DB_USER};Password=${PROD_DB_PASSWORD};"

                        dotnet run \
                          --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                          --configuration Release -- \
                          --source "$SOURCE_CONN" \
                          --target "$TARGET_CONN" \
                          --title "${service} Schema Comparison Report (QA vs PROD)" \
                          --out "gh-pages/qa-vs-prod/${service}.html"

                    done
                '''
            }
        }

        stage('Run Schema Comparison QA vs TEST') {
            steps {
                sh '''
                    set -e
                    set -x

                    services=("common" "community" "inventory" "payroll" "purchase" "sales")

                    mkdir -p gh-pages/qa-vs-test

                    for service in "${services[@]}"; do

                        echo "Comparing QA → TEST for $service..."

                        SOURCE_CONN="Server=${QA_DB_HOST};Port=${QA_DB_PORT};Database=qa-dhanman-${service};User Id=${QA_DB_USER};Password=${QA_DB_PASSWORD};"

                        TARGET_CONN="Server=${TEST_DB_HOST};Port=${TEST_DB_PORT};Database=test-dhanman-${service};User Id=${TEST_DB_USER};Password=${TEST_DB_PASSWORD};"

                        dotnet run \
                          --project src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                          --configuration Release -- \
                          --source "$SOURCE_CONN" \
                          --target "$TARGET_CONN" \
                          --title "${service} Schema Comparison Report (QA vs TEST)" \
                          --out "gh-pages/qa-vs-test/${service}.html"

                    done
                '''
            }
        }

        stage('Validate Reports') {
            steps {
                sh '''
                    set -e

                    ls -R gh-pages || true

                    if [ ! -d "gh-pages/qa-vs-prod" ] || [ -z "$(ls -A gh-pages/qa-vs-prod)" ]; then
                        echo "No reports generated for QA vs PROD"
                        exit 1
                    fi

                    if [ ! -d "gh-pages/qa-vs-test" ] || [ -z "$(ls -A gh-pages/qa-vs-test)" ]; then
                        echo "No reports generated for QA vs TEST"
                        exit 1
                    fi
                '''
            }
        }

        stage('Deploy Schema Reports to OVH') {
            steps {
                sh '''
                    sshpass -p "${DO_PASSWORD}" scp -o StrictHostKeyChecking=no -r gh-pages/* \
                    ${DO_USER}@${DO_HOST}:/var/www/dbtula-site
                '''
            }
        }

    }

    post {
        success {
            echo "Schema comparison completed successfully!"
        }
        failure {
            echo "Schema comparison failed!"
        }
    }
}
