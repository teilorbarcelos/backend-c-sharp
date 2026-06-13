#!/usr/bin/env bash
set -uo pipefail

PROJECT_KEY="${1:-backend-c-sharp}"
PROJECT_NAME="${2:-backend-c-sharp}"
TESTS_PROJECT="${TESTS_PROJECT:-tests/MageBackend.Tests.csproj}"
TEST_TIMEOUT="${TEST_TIMEOUT:-900}"
EXCLUSIONS="${EXCLUSIONS:-**/Migrations/**,**/obj/**,**/bin/**}"
SONAR_TOKEN="${SONAR_TOKEN:-squ_20f4835a20b16839fe7a52b4d43ff97224e640d1}"
SONAR_HOST="${SONAR_HOST:-http://localhost:9000}"
SCANNER_BIN="${SCANNER_BIN:-/home/teilor/.sonar/native-sonar-scanner/sonar-scanner-6.2.1.4610-linux-x64/bin/sonar-scanner}"

export LANG="${LANG:-C.UTF-8}"
export LC_ALL="${LC_ALL:-C.UTF-8}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "=========================================="
echo " SonarQube two-step scan"
echo "  Project:    $PROJECT_KEY"
echo "  Name:       $PROJECT_NAME"
echo "  Tests proj: $TESTS_PROJECT"
echo "  LANG:       $LANG"
echo "=========================================="

rm -rf .sonarqube
rm -f tests/coverage.cobertura.xml tests/coverage.generic.xml tests/coverage.opencover.xml

echo ""
echo ">> Step 1/3: dotnet sonarscanner begin"
dotnet sonarscanner begin \
  /k:"$PROJECT_KEY" \
  /n:"$PROJECT_NAME" \
  /d:sonar.host.url="$SONAR_HOST" \
  /d:sonar.token="$SONAR_TOKEN" \
  /d:sonar.exclusions="$EXCLUSIONS" \
  /d:sonar.cs.opencover.reportsPaths="tests/coverage.opencover.xml"

echo ""
echo ">> Step 2/3: build + tests with coverage (allow failures)"
set +e
timeout "$TEST_TIMEOUT" dotnet test "$TESTS_PROJECT" -m:1 \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat="cobertura%2copencover" \
  /p:CoverletOutput=./tests/coverage \
  /p:IncludeTestAssembly="false" || echo "WARN: tests reported failures (exit $?)"
set -e

echo ""
echo ">> Step 3a/3: dotnet sonarscanner end (C# analysis)"
dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN" || echo "WARN: sonarscanner end failed (exit $?)"

echo ""
echo ">> Step 3b/3: convert cobertura -> generic + generic scanner (coverage)"
COVERAGE_FILE="tests/coverage.cobertura.xml"
if [ -f "tests/tests/coverage.cobertura.xml" ]; then
  COVERAGE_FILE="tests/tests/coverage.cobertura.xml"
fi

if [ -f "$COVERAGE_FILE" ]; then
  python3 - "$COVERAGE_FILE" <<'PY'
import sys, os
import xml.etree.ElementTree as ET
src = sys.argv[1]
tree = ET.parse(src); root = tree.getroot()
src_base = os.path.join(os.getcwd(), 'src') + '/'
gen_root = ET.Element('coverage', version='1')
files = 0
for pkg in root.findall('.//package'):
    for cls in pkg.findall('./classes/class'):
        fn = cls.get('filename','')
        if src_base and fn.startswith(src_base): fn = fn[len(src_base):]
        if not fn or not fn.endswith('.cs'): continue
        ld = [(l.get('number'),l.get('hits','0')) for l in cls.findall('.//lines/line')]
        if not ld: continue
        fe = ET.SubElement(gen_root, 'file', path='src/'+fn)
        for n,h in ld:
            ET.SubElement(fe, 'lineToCover', lineNumber=n, covered='true' if int(h)>0 else 'false')
        files += 1
ET.ElementTree(gen_root).write('tests/coverage.generic.xml', encoding='utf-8', xml_declaration=True)
print(f"Converted {files} .cs files to generic format")
PY

  rm -rf .sonarqube
  "$SCANNER_BIN" \
    -Dsonar.host.url="$SONAR_HOST" \
    -Dsonar.token="$SONAR_TOKEN" \
    -Dsonar.projectKey="$PROJECT_KEY" \
    -Dsonar.projectName="$PROJECT_NAME" \
    -Dsonar.sources="src" \
    -Dsonar.exclusions="$EXCLUSIONS" \
    -Dsonar.coverageReportPaths="tests/coverage.generic.xml" || echo "WARN: generic scanner failed (exit $?)"
else
  echo "WARN: coverage report not found at $COVERAGE_FILE; skipping coverage injection"
fi

echo ""
echo "=========================================="
echo " Done. Dashboard:"
echo "  $SONAR_HOST/dashboard?id=$PROJECT_KEY"
echo "=========================================="
