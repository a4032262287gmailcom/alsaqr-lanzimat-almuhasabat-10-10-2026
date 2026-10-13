from flask import Flask, render_template, request
from pathlib import Path
import csv
from collections import Counter

app = Flask(__name__)
REPO_ROOT = Path(__file__).resolve().parents[1]


def load_function_inventory():
    inventory_files = sorted(REPO_ROOT.glob('ERP_FunctionInventory_*.csv'))
    if not inventory_files:
        return []
    path = inventory_files[0]
    with path.open('r', encoding='utf-8-sig', newline='') as fh:
        reader = csv.DictReader(fh)
        rows = []
        for row in reader:
            if not row:
                continue
            rows.append({
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
            })
    return rows


def load_event_handlers():
    event_files = sorted(REPO_ROOT.glob('ERP_EventHandlers_*.csv'))
    if not event_files:
        return []
    path = event_files[0]
    with path.open('r', encoding='utf-8-sig', newline='') as fh:
        reader = csv.DictReader(fh)
        rows = []
        for row in reader:
            if not row:
                continue
            rows.append({
                'file': (row.get('File') or '').strip(),
                'type': (row.get('Type') or '').strip(),
                'line': (row.get('Line') or '').strip(),
                'control_or_target': (row.get('ControlOrTarget') or '').strip(),
                'event': (row.get('Event') or '').strip(),
                'handler': (row.get('Handler') or '').strip(),
                'source_line': (row.get('SourceLine') or '').strip(),
            })
    return rows


def load_function_details_summary():
    detail_files = sorted(REPO_ROOT.glob('ERP_FunctionDetails_*.txt'))
    if not detail_files:
        return {}
    text = detail_files[0].read_text(encoding='utf-8-sig')
    summary = {}
    for key, pattern in {
        'files_scanned': r'C# files scanned:\s*(\d+)',
        'type_declarations': r'Type declarations found:\s*(\d+)',
        'method_declarations': r'Method declarations found:\s*(\d+)',
        'event_hookups': r'Event hookup lines found:\s*(\d+)',
    }.items():
        m = __import__('re').search(pattern, text)
        if m:
            summary[key] = int(m.group(1))
    return summary


def filter_rows(rows, query):
    q = (query or '').strip().lower()
    if not q:
        return rows
    return [
        row for row in rows
        if q in str(row.get('file', '')).lower()
        or q in str(row.get('namespace', '')).lower()
        or q in str(row.get('type', '')).lower()
        or q in str(row.get('method', '')).lower()
        or q in str(row.get('behavior_guess', '')).lower()
        or q in str(row.get('calls_found', '')).lower()
        or q in str(row.get('event', '')).lower()
        or q in str(row.get('handler', '')).lower()
    ]


@app.route('/')
def index():
    methods = load_function_inventory()
    events = load_event_handlers()
    summary = load_function_details_summary()
    q = request.args.get('q', '').strip()
    filtered_methods = filter_rows(methods, q)
    filtered_events = filter_rows(events, q)

    namespaces = Counter(row['namespace'] for row in methods if row.get('namespace'))
    behaviors = Counter(row['behavior_guess'] for row in methods if row.get('behavior_guess'))

    overall = {
        'files_scanned': summary.get('files_scanned', 0),
        'type_declarations': summary.get('type_declarations', 0),
        'method_declarations': summary.get('method_declarations', 0),
        'event_hookups': summary.get('event_hookups', 0),
        'functions_count': len(methods),
        'events_count': len(events),
        'filtered_functions': len(filtered_methods),
        'filtered_events': len(filtered_events),
    }

    return render_template(
        'index.html',
        summary=overall,
        methods=filtered_methods[:200],
        events=filtered_events[:200],
        top_namespaces=namespaces.most_common(10),
        top_behaviors=behaviors.most_common(10),
        query=q,
    )


if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=True)
