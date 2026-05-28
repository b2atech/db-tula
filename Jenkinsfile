pipeline {
    agent any

    options {
        buildDiscarder(logRotator(daysToKeepStr: '7', numToKeepStr: '20'))
        timeout(time: 30, unit: 'MINUTES')
    }

    triggers {
        cron('30 18 * * *') // midnight IST
    }

    environment {
        DBTULA           = './publish/B2A.DbTula.Cli'
        DBTULA_SMTP_HOST = 'smtp.gmail.com'
        DBTULA_SMTP_PORT = '587'
        DBTULA_SMTP_USE_SSL = 'true'
    }

    stages {

        stage('Checkout') {
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

        stage('Build & Publish') {
            steps {
                sh '''
                    dotnet restore src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj

                    dotnet publish src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                        --configuration Release \
                        --runtime linux-x64 \
                        --self-contained true \
                        -p:PublishSingleFile=true \
                        -o ./publish/

                    chmod +x ./publish/B2A.DbTula.Cli
                '''
            }
        }

        stage('Unit Tests') {
            steps {
                sh '''
                    dotnet test tests/B2A.DbTula.Core.Tests/B2A.DbTula.Core.Tests.csproj \
                        --configuration Release \
                        --logger "junit;LogFilePath=../../test-results/unit-tests.xml" \
                        || true
                '''
            }
            post {
                always {
                    junit allowEmptyResults: true, testResults: 'test-results/unit-tests.xml'
                }
            }
        }

        stage('QA vs PROD') {
            steps {
                sh 'mkdir -p gh-pages/qa-vs-prod'
                withCredentials([
                    // QA connection strings
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMONDB_QA',    variable: 'COMMONDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMUNITYDB_QA',  variable: 'COMMUNITYDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__INVENTORYDB_QA',  variable: 'INVENTORYDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PAYROLLDB_QA',    variable: 'PAYROLLDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PURCHASEDB_QA',   variable: 'PURCHASEDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__SALESDB_QA',      variable: 'SALESDB_QA'),
                    // PROD connection strings
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMONDB_PROD',   variable: 'COMMONDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMUNITYDB_PROD', variable: 'COMMUNITYDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__INVENTORYDB_PROD', variable: 'INVENTORYDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PAYROLLDB_PROD',  variable: 'PAYROLLDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PURCHASEDB_PROD', variable: 'PURCHASEDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__SALESDB_PROD',    variable: 'SALESDB_PROD'),
                    // Email
                    string(credentialsId: 'DBTULA_SMTP_USER', variable: 'DBTULA_SMTP_USER'),
                    string(credentialsId: 'DBTULA_SMTP_PASS', variable: 'DBTULA_SMTP_PASS'),
                    string(credentialsId: 'DBTULA_SMTP_TO',   variable: 'DBTULA_SMTP_TO'),
                ]) {
                    script {
                        // DBTULA_SMTP_FROM = same as user
                        env.DBTULA_SMTP_FROM = env.DBTULA_SMTP_USER

                        def exitCode = sh(returnStatus: true, script: """
                            ${DBTULA} --batch batch-qa-vs-prod.json --fail-on-drift
                        """)

                        if (exitCode == 2) {
                            currentBuild.result = 'UNSTABLE'
                            echo '🚨 QA vs PROD: objects missing in PROD — build marked UNSTABLE'
                        } else if (exitCode == 1) {
                            currentBuild.result = 'UNSTABLE'
                            echo '⚠️  QA vs PROD: schema mismatches detected — build marked UNSTABLE'
                        } else if (exitCode >= 3) {
                            error('❌ QA vs PROD batch failed — check logs for connection errors')
                        } else {
                            echo '✅ QA vs PROD: no drift detected'
                        }
                    }
                }
            }
        }

        stage('Archive Reports') {
            steps {
                archiveArtifacts artifacts: 'gh-pages/**/*.html,gh-pages/**/*.sql',
                    allowEmptyArchive: true
            }
        }

        stage('Deploy to OVH') {
            steps {
                withCredentials([
                    string(credentialsId: 'DO_HOST', variable: 'DO_HOST'),
                    string(credentialsId: 'DO_USER', variable: 'DO_USER'),
                ]) {
                    sshagent(credentials: ['DO_SSH_KEY']) {
                        sh '''
                            scp -o StrictHostKeyChecking=no -r gh-pages/* \
                                ${DO_USER}@${DO_HOST}:/var/www/dbtula-site
                        '''
                    }
                }
            }
        }

    }

    post {
        always {
            cleanWs()
        }
        unstable {
            echo '⚠️  Schema drift detected — review reports on the OVH site'
        }
        failure {
            echo '❌ Pipeline failed — check logs for connection or build errors'
        }
        success {
            echo '✅ All schema comparisons completed — no drift detected'
        }
    }
}
