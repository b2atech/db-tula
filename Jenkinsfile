pipeline {
    agent any

    options {
        buildDiscarder(logRotator(daysToKeepStr: '7', numToKeepStr: '20'))
        timeout(time: 45, unit: 'MINUTES')
    }

    triggers {
        cron('30 18 * * *')         // midnight IST — nightly comparison only
        pollSCM('H/5 * * * *')      // check for commits every 5 min → deploy
    }

    environment {
        DBTULA          = './publish/B2A.DbTula.Cli'
        DBTULA_SMTP_HOST    = 'smtp.gmail.com'
        DBTULA_SMTP_PORT    = '587'
        DBTULA_SMTP_USE_SSL = 'true'
        APP_SERVER      = 'ubuntu@57.129.74.139'
        IS_CRON         = "${currentBuild.getBuildCauses('hudson.triggers.TimerTrigger') ? 'true' : 'false'}"
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

        // ── Always: build CLI for nightly comparison ──────────────────────
        stage('Build CLI') {
            steps {
                sh '''
                    dotnet publish src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                        --configuration Release \
                        --runtime linux-x64 \
                        --self-contained true \
                        -o ./publish/
                    chmod +x ./publish/B2A.DbTula.Cli
                '''
            }
        }

        // ── On commit only: build API ─────────────────────────────────────
        stage('Build API') {
            when { not { triggeredBy 'TimerTrigger' } }
            steps {
                sh '''
                    dotnet publish src/B2A.DbTula.Api/B2A.DbTula.Api.csproj \
                        --configuration Release \
                        --runtime linux-x64 \
                        --self-contained false \
                        -o ./publish-api/
                '''
            }
        }

        // ── On commit only: build React UI ────────────────────────────────
        stage('Build React UI') {
            when { not { triggeredBy 'TimerTrigger' } }
            steps {
                withCredentials([
                    string(credentialsId: 'VITE_GOOGLE_CLIENT_ID', variable: 'VITE_GOOGLE_CLIENT_ID'),
                ]) {
                    sh '''
                        cd web/dbtula-web
                        npm ci --prefer-offline
                        npm run build
                    '''
                }
            }
        }

        // ── On commit only: unit tests ────────────────────────────────────
        stage('Unit Tests') {
            when { not { triggeredBy 'TimerTrigger' } }
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

        // ── On commit only: deploy to dbtula.dgtula.com ───────────────────
        stage('Deploy to dbtula.dgtula.com') {
            when { not { triggeredBy 'TimerTrigger' } }
            steps {
                sshagent(credentials: ['DO_FALLBACK_HOST']) {
                    sh '''
                        echo "=== Deploying API ==="
                        rsync -az --delete \
                            -e "ssh -o StrictHostKeyChecking=no" \
                            ./publish-api/ \
                            ${APP_SERVER}:/var/www/dbtula-api/

                        echo "=== Deploying React UI ==="
                        ssh -o StrictHostKeyChecking=no ${APP_SERVER} \
                            "sudo chown -R ubuntu:ubuntu /var/www/dbtula-web"
                        rsync -az --delete \
                            -e "ssh -o StrictHostKeyChecking=no" \
                            ./web/dbtula-web/dist/ \
                            ${APP_SERVER}:/var/www/dbtula-web/
                        ssh -o StrictHostKeyChecking=no ${APP_SERVER} \
                            "sudo chown -R www-data:www-data /var/www/dbtula-web"

                        echo "=== Restarting API ==="
                        ssh -o StrictHostKeyChecking=no ${APP_SERVER} \
                            "sudo systemctl restart dbtula-api && sleep 3 && sudo systemctl is-active dbtula-api"
                    '''
                }
            }
        }

        // ── Always: nightly comparison (all 9 DBs) ───────────────────────
        stage('QA vs PROD') {
            steps {
                sh 'mkdir -p gh-pages/qa-vs-prod'
                withCredentials([
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMONDB_QA',     variable: 'COMMONDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMUNITYDB_QA',  variable: 'COMMUNITYDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__INVENTORYDB_QA',  variable: 'INVENTORYDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PAYROLLDB_QA',    variable: 'PAYROLLDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PURCHASEDB_QA',   variable: 'PURCHASEDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__SALESDB_QA',      variable: 'SALESDB_QA'),
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMONDB_PROD',   variable: 'COMMONDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__COMMUNITYDB_PROD',variable: 'COMMUNITYDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__INVENTORYDB_PROD',variable: 'INVENTORYDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PAYROLLDB_PROD',  variable: 'PAYROLLDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__PURCHASEDB_PROD', variable: 'PURCHASEDB_PROD'),
                    string(credentialsId: 'CONNECTIONSTRINGS__SALESDB_PROD',    variable: 'SALESDB_PROD'),
                    string(credentialsId: 'DBTULA_SMTP_USER', variable: 'DBTULA_SMTP_USER'),
                    string(credentialsId: 'DBTULA_SMTP_PASS', variable: 'DBTULA_SMTP_PASS'),
                    string(credentialsId: 'DBTULA_SMTP_TO',   variable: 'DBTULA_SMTP_TO'),
                ]) {
                    script {
                        env.DBTULA_SMTP_FROM = env.DBTULA_SMTP_USER
                        def exitCode = sh(returnStatus: true, script: """
                            ${DBTULA} --batch batch-qa-vs-prod.json --fail-on-drift
                        """)
                        if (exitCode == 2) {
                            currentBuild.result = 'UNSTABLE'
                            echo '🚨 Objects missing in PROD — check dbtula.dgtula.com'
                        } else if (exitCode == 1) {
                            currentBuild.result = 'UNSTABLE'
                            echo '⚠️  Schema mismatches detected — check dbtula.dgtula.com'
                        } else if (exitCode >= 3) {
                            error('❌ Batch failed — connection errors')
                        } else {
                            echo '✅ No drift detected'
                        }
                    }
                }
            }
        }

        // ── Always: push comparison results into UI dashboard ────────────
        stage('Sync to UI Dashboard') {
            steps {
                withCredentials([
                    string(credentialsId: 'DBTULA_API_KEY', variable: 'DBTULA_API_KEY'),
                ]) {
                    script {
                        def code = sh(returnStdout: true, script: '''
                            curl -s -o /dev/null -w "%{http_code}" \
                                -X POST "http://57.129.74.139/api/scheduled/trigger-all" \
                                -H "X-Api-Key: ${DBTULA_API_KEY}"
                        ''').trim()
                        echo code == '200'
                            ? "✅ UI dashboard queued all 9 comparison runs"
                            : "⚠️  UI trigger returned HTTP ${code}"
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

    }

    post {
        always { cleanWs() }
        unstable { echo '⚠️  Drift detected — review dbtula.dgtula.com' }
        failure  { echo '❌ Pipeline failed — check logs' }
        success  { echo '✅ Complete' }
    }
}
