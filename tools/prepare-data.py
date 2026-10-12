#!/usr/bin/env python3
"""Explicit pinned download + local fixture query extraction; no database/model calls or overwrites."""
import hashlib
import json
from pathlib import Path
import sys
import urllib.request

URL = 'https://raw.githubusercontent.com/Azure-Samples/documentdb-samples/f5a2675bbf349737234d0391e3ebee24b260e06f/ai/data/Hotels_Vector.json'
SHA256 = '60c08c2cdd439444ba8601d94a1f3487f4a9ee56cd48ba201d983c96722de6e4'


def main():
    directory = Path('data')
    data_path, query_path = directory / 'Hotels_Vector.json', directory / 'query-vector.json'
    if data_path.exists() or query_path.exists():
        raise ValueError('Refusing overwrite: review existing data/query files or use explicit fixture path environment variables.')
    with urllib.request.urlopen(URL, timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != SHA256:
        raise ValueError('Pinned fixture checksum mismatch.')
    rows = json.loads(data)
    if len(rows) != 50 or any(len(row['DescriptionVector']) != 1536 for row in rows):
        raise ValueError('Unexpected pinned fixture shape.')
    query = json.dumps(rows[0]['DescriptionVector'], separators=(',', ':')) + '\n'
    directory.mkdir(exist_ok=True)
    with data_path.open('xb') as output:
        output.write(data)
    with query_path.open('x') as output:
        output.write(query)
    print('Verified 50 x 1536 fixture; extracted first-record query. No model call or semantic-quality claim.')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(f'Data setup failed ({type(error).__name__}); inspect the pinned source and existing local paths. No existing file was overwritten.', file=sys.stderr)
        sys.exit(1)
