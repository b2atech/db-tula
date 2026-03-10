pipeline {
    agent any

    options {
        buildDiscarder(logRotator(daysToKeepStr: '3', numToKeepStr: '10'))
    }

    // Runs daily at midnight IST
    triggers {
        cron('30 18 * * *')
    }

    parameters {
        string(name: 'BRANCH_TO_BUILD', defaultValue: '', description: 'Branch to build (leave blank for default)')
    }

    environment {

        // QA DB
        QA_DB_HOST     = credentials('QA_DB_HOST')
        QA_DB_PORT     = credentials('QA_DB_PORT')
        QA_DB_USER     = credentials('QA_DB_USER')
        QA_DB_PASSWORD = credentials('QA_DB_PASSWORD')

        // PROD DB
        PROD_DB_HOST     = credentials('PROD_DB_HOST')
        PROD_DB_PORT     = credentials('PROD_DB_PORT')
        PROD_DB_USER     = credentials('PROD_DB_USER')
        PROD_DB_PASSWORD = credentials('PROD_DB_PASSWORD')

        // TEST DB
        TEST_DB_HOST     = credentials('TEST_DB_HOST')
        TEST_DB_PORT     = credentials('TEST_DB_PORT')
        TEST_DB_USER     = credentials('TEST_DB_USER')
        TEST_DB_PASSWORD = credentials('TEST_DB_PASSWORD')

        DO_HOST       = credentials('DO_HOST')
        DO_USER       = credentials('DO_USER')
        DO_SSH_KEY_ID = 'DO_SSH_KEY'

        PATH = "$HOME/.dotnet:$PATH"
    }

    stages {

        // ----------------------------------------------------
        // Checkout
        // ----------------------------------------------------
        stage('Checkout') {
            steps {
                script {
                    if (params.BRANCH_TO_BUILD?.trim()) {
                        checkout([
                            $class: 'GitSCM',
                            branches: [[name: params.BRANCH_TO_BUILD]],
                            userRemoteConfigs: [[
                                url: 'https://github.com/b2atech/db-tula.git',
                                credentialsId: 'github-b2a'
                            ]]
                        ])
                    } else {
                        checkout([
                            $class: 'GitSCM',
                            branches: [[name: '*/main']],
                            userRemoteConfigs: [[
                                url: 'https://github.com/b2atech/db-tula.git',
                                credentialsId: 'github-b2a'
                            ]]
                        ])
                    }
                }
            }
        }

        // ----------------------------------------------------
        // Setup .NET SDK
        // ----------------------------------------------------
        stage('Setup .NET SDK') {
            steps {
                sh '''
                    set -e
                    wget https://dot.net/v1/dotnet-install.sh
                    chmod +x dotnet-install.sh
                    ./dotnet-install.sh --channel 9.0
                '''
            }
        }

        // ----------------------------------------------------
        // Restore Dependencies
        // ----------------------------------------------------
        stage('Restore Dependencies') {
            steps {
                sh '''
                    set -e
                    dotnet restore src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj
                '''
            }
        }

        // ----------------------------------------------------
        // Build CLI
        // ----------------------------------------------------
        stage('Build CLI Tool') {
            steps {
                sh '''
                    set -e
                    dotnet build src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                    --configuration Release
                '''
            }
        }

        // ----------------------------------------------------
        // QA vs PROD Comparison
        // ----------------------------------------------------
        stage('QA vs PROD Schema Comparison') {
            steps {
                sh '''
                    set -e

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

        // ----------------------------------------------------
        // QA vs TEST Comparison
        // ----------------------------------------------------
        stage('QA vs TEST Schema Comparison') {
            steps {
                sh '''
                    set -e

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

        // ----------------------------------------------------
        // Validate Reports
        // ----------------------------------------------------
        stage('Validate Reports') {
            steps {
                sh '''
                    set -e

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

        // ----------------------------------------------------
        // Deploy Reports
        // ----------------------------------------------------
        stage('Deploy Reports') {
            steps {
                sshagent(credentials: [env.DO_SSH_KEY_ID]) {
                    sh """
                        scp -o StrictHostKeyChecking=no -r gh-pages/* \
                        ${DO_USER}@${DO_HOST}:/var/www/dbtula-site
                    """
                }
            }
        }
    }

    post {

        success {
            echo 'Schema comparison completed successfully!'
        }

        failure {
            echo 'Schema comparison failed!'
        }

        always {
            node {
                deleteDir()
            }
        }
    }
}
