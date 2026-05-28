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
        // QA connection strings
        COMMONDB_QA     = credentials('CONNECTIONSTRINGS__COMMONDB_QA')
        COMMUNITYDB_QA  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_QA')
        INVENTORYDB_QA  = credentials('CONNECTIONSTRINGS__INVENTORYDB_QA')
        PAYROLLDB_QA    = credentials('CONNECTIONSTRINGS__PAYROLLDB_QA')
        PURCHASEDB_QA   = credentials('CONNECTIONSTRINGS__PURCHASEDB_QA')
        SALESDB_QA      = credentials('CONNECTIONSTRINGS__SALESDB_QA')

        // PROD connection strings
        COMMONDB_PROD     = credentials('CONNECTIONSTRINGS__COMMONDB_PROD')
        COMMUNITYDB_PROD  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_PROD')
        INVENTORYDB_PROD  = credentials('CONNECTIONSTRINGS__INVENTORYDB_PROD')
        PAYROLLDB_PROD    = credentials('CONNECTIONSTRINGS__PAYROLLDB_PROD')
        PURCHASEDB_PROD   = credentials('CONNECTIONSTRINGS__PURCHASEDB_PROD')
        SALESDB_PROD      = credentials('CONNECTIONSTRINGS__SALESDB_PROD')

        // TEST connection strings
        COMMONDB_TEST     = credentials('CONNECTIONSTRINGS__COMMONDB_TEST')
        COMMUNITYDB_TEST  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_TEST')
        INVENTORYDB_TEST  = credentials('CONNECTIONSTRINGS__INVENTORYDB_TEST')
        PAYROLLDB_TEST    = credentials('CONNECTIONSTRINGS__PAYROLLDB_TEST')
        PURCHASEDB_TEST   = credentials('CONNECTIONSTRINGS__PURCHASEDB_TEST')
        SALESDB_TEST      = credentials('CONNECTIONSTRINGS__SALESDB_TEST')

        // Email (Gmail App Password) — set in Jenkins credentials
        DBTULA_SMTP_HOST = 'smtp.gmail.com'
        DBTULA_SMTP_PORT = '587'
        DBTULA_SMTP_USER = credentials('DBTULA_SMTP_USER')
        DBTULA_SMTP_PASS = credentials('DBTULA_SMTP_PASS')
        DBTULA_SMTP_FROM = credentials('DBTULA_SMTP_USER')
        DBTULA_SMTP_TO   = credentials('DBTULA_SMTP_TO')

        DO_HOST       = credentials('DO_HOST')
        DO_USER       = credentials('DO_USER')
        DO_SSH_KEY_ID = 'DO_SSH_KEY'

        DBTULA = './publish/B2A.DbTula.Cli'
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

        // QA-vs-PROD and QA-vs-TEST run in parallel — each batch runs its services sequentially.
        // Email is sent once per batch (QA-vs-PROD only) if any drift is found.
        stage('Schema Comparisons') {
            parallel {

                stage('QA vs PROD') {
                    steps {
                        sh 'mkdir -p gh-pages/qa-vs-prod'
                        script {
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
                            }
                        }
                    }
                }

                stage('QA vs TEST') {
                    steps {
                        sh 'mkdir -p gh-pages/qa-vs-test'
                        sh "${DBTULA} --batch batch-qa-vs-test.json || true"
                        // QA-vs-TEST: informational only — does not fail or send email
                    }
                }

            } // end parallel
        }

        stage('Archive Reports') {
            steps {
                archiveArtifacts artifacts: 'gh-pages/**/*.html,gh-pages/**/*.sql',
                    allowEmptyArchive: true
            }
        }

        stage('Deploy to OVH') {
            steps {
                sshagent(credentials: ['DO_SSH_KEY']) {
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
        unstable {
            echo '⚠️  Schema drift detected in QA vs PROD — review reports on the OVH site'
            // Uncomment to add Slack notification:
            // slackSend channel: '#deploys', color: 'warning',
            //   message: "dbtula: Schema drift detected on ${env.JOB_NAME} — ${env.BUILD_URL}"
        }
        failure {
            echo '❌ Comparison pipeline failed — check logs for connection or build errors'
        }
        success {
            echo '✅ All schema comparisons completed — no drift detected'
        }
    }
}
