#!/usr/bin/env python3
"""Fail closed on NuGet advisory results, including transitives (list itself exits 0 on findings)."""
import json
import subprocess
import sys


def validate(report):
    if report.get('version') != 1 or not report.get('projects') or not report.get('sources'):
        raise ValueError('Incomplete NuGet advisory response')
    if report.get('problems'):
        raise ValueError('NuGet advisory query reported problems')
    for project in report['projects']:
        for framework in project.get('frameworks', []):
            for group in ('topLevelPackages', 'transitivePackages'):
                for package in framework.get(group, []):
                    if package.get('vulnerabilities'):
                        raise ValueError('Vulnerable NuGet dependency detected')


def main():
    for project in ('DocumentDBVectorSearch.csproj', 'tests/DocumentDBVectorSearch.Tests.csproj'):
        command = ['dotnet', 'list', project, 'package', '--vulnerable', '--include-transitive',
                   '--source', 'https://api.nuget.org/v3/index.json', '--format', 'json']
        result = subprocess.run(command, capture_output=True, text=True, check=True, timeout=180)
        validate(json.loads(result.stdout))
        print(f'PASS current-feed NuGet direct + transitive audit: {project}')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('FAIL NuGet advisory gate: vulnerability, incomplete result, or feed/tool failure', file=sys.stderr)
        sys.exit(1)
