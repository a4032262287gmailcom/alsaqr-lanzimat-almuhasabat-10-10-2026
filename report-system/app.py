from flask import Flask, render_template, request, send_file
from pathlib import Path
from collections import Counter
import csv
import io
import re

app = Flask(__name__)

ROOT_DIR = Path(__file__).resolve().parents[1]


def find_report_files(pattern: str):
    matches = sorted(ROOT_DIR.glob(pattern))
    return matches[0] if matches else None


def load_summary():
    details_file = find_report_files('ERP_FunctionDetails_*.txt')
    if not details_file:
        return {}

    text = details_file.read_text(encoding='utf-8-sig')
    summary = {}
    patterns = {
        'files_scanned': r'C# files scanned:\s*(\d+)',
        'type_declarations': r'Type declarations found:\s*(\d+)',
        'method_declarations': r'Method declarations found:\s*(\d+)',
        'event_hookups': r'Event hookup lines found:\s*(\d+)',
    }
    for key, pattern in patterns.items():
        match = re.search(pattern, text, re.IGNORECASE)
        if match:
            summary[key] = int(match.group(1))
    return summary


def load_functions():
    file_path = find_report_files('ERP_FunctionInventory_*.csv')
    if file_path is None:
        return []

    rows = []
    with file_path.open('r', encoding='utf-8-sig', newline='') as handle:
        reader = csv.DictReader(handle)
        for idx, row in enumerate(reader, start=1):
            if row is None:
                continue
            if not any((value or '').strip() for value in row.values()):
                continue
            clean = {
                'id': idx,
                'file': (row.get('File') or '').strip(),
                'namespace': (row.get('Namespace') or '').strip(),
                'type': (row.get('Type') or '').strip(),
                'method': (row.get('Method') or '').strip(),
                'access': (row.get('Access') or '').strip(),
                'return_type': (row.get('ReturnType') or '').strip(),
                'parameters': (row.get('Parameters') or '').strip(),
                'line': (row.get('Line') or '').strip(),
                'behavior_guess': (row.get('BehaviorGuess') or '').strip(),
                'calls_found': (row.get('CallsFound') or '').strip(),
                'code_preview': (row.get('CodePreview') or '').strip(),
            }
            rows.append(clean)
    return rows


def load_events():
    file_path = find_report_files('ERP_EventHandlers_*.csv')
    if file_path is None:
        return []

    rows = []
    with file_path.open('r', encoding='utf-8-sig', newline='') as handle:
        reader = csv.DictReader(handle)
        for idx, row in enumerate(reader, start=1):
            if row is None:
                continue
            if not any((value or '').strip() for value in row.values()):
                continue
            clean = {
                'id': idx,
                'file': (row.get('File') or '').strip(),
                'type': (row.get('Type') or '').strip(),
                'line': (row.get('Line') or '').strip(),
                'control_or_target': (row.get('ControlOrTarget') or '').strip(),
                'event': (row.get('Event') or '').strip(),
                'handler': (row.get('Handler') or '').strip(),
                'source_line': (row.get('SourceLine') or '').strip(),
            }
            rows.append(clean)
    return rows


def filter_rows(rows, query):
    q = (query or '').strip().lower()
    if not q:
        return rows
    filtered = []
    for row in rows:
        haystack = ' '.join(
            str(row.get(key, '') or '')
            for key in row.keys()
        ).lower()
        if q in haystack:
            filtered.append(row)
    return filtered


@app.route('/')
def index():
    functions = load_functions()
    events = load_events()
    summary = load_summary()

    q = request.args.get('q', '').strip()
    functions = filter_rows(functions, q)
    events = filter_rows(events, q)

    namespaces = Counter(row.get('namespace', '') for row in functions if row.get('namespace'))
    behaviors = Counter(row.get('behavior_guess', '') for row in functions if row.get('behavior_guess'))

    payload = {
        'files_scanned': summary.get('files_scanned', 0),
        'type_declarations': summary.get('type_declarations', 0),
        'method_declarations': summary.get('method_declarations', 0),
        'event_hookups': summary.get('event_hookups', 0),
        'functions_count': len(load_functions()),
        'events_count': len(load_events()),
        'filtered_functions': len(functions),
        'filtered_events': len(events),
    }

    return render_template(
        'index.html',
        summary=payload,
        functions=functions[:200],
        events=events[:200],
        top_namespaces=namespaces.most_common(8),
        top_behaviors=behaviors.most_common(8),
        query=q,
    )


@app.route('/function/<int:id>')
def function_detail(id):
    function = next((row for row in load_functions() if row.get('id') == id), None)
    if function is None:
        return 'غير موجود', 404
    return render_template('detail.html', function=function)


@app.route('/export/functions')
def export_functions():
    q = request.args.get('q', '').strip()
    rows = filter_rows(load_functions(), q)

    output = io.StringIO()
    writer = csv.writer(output)
    writer.writerow([
        'id', 'file', 'namespace', 'type', 'method', 'access', 'return_type',
        'parameters', 'line', 'behavior_guess', 'calls_found', 'code_preview'
    ])
    for row in rows:
        writer.writerow([
            row.get('id', ''),
            row.get('file', ''),
            row.get('namespace', ''),
            row.get('type', ''),
            row.get('method', ''),
            row.get('access', ''),
            row.get('return_type', ''),
            row.get('parameters', ''),
            row.get('line', ''),
            row.get('behavior_guess', ''),
            row.get('calls_found', ''),
            row.get('code_preview', ''),
        ])

    binary = output.getvalue().encode('utf-8-sig')
    buffer = io.BytesIO(binary)
    return send_file(
        buffer,
        mimetype='text/csv',
        as_attachment=True,
        download_name='functions_export.csv'
    )


@app.route('/health')
def health_check():
    return {'status': 'ok', 'repo_root': str(ROOT_DIR)}, 200


if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=True)

