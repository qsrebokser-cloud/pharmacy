from pathlib import Path
import sqlite3
import json
from decimal import Decimal, ROUND_HALF_UP
from docx import Document
from pypdf import PdfReader

root = Path(__file__).resolve().parent.parent
with sqlite3.connect(':memory:') as restored:
    restored.executescript((root / 'DatabaseWithData.sql').read_text(encoding='utf-8'))
    assert restored.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    assert not restored.execute('PRAGMA foreign_key_check').fetchall()
    assert restored.execute('SELECT COUNT(*) FROM Users').fetchone()[0] == 8
    assert restored.execute('SELECT COUNT(*) FROM Products').fetchone()[0] == 15
    assert restored.execute('SELECT COUNT(*) FROM Orders').fetchone()[0] == 7
    assert restored.execute('SELECT COUNT(*) FROM OrderItems').fetchone()[0] == 12
    total = restored.execute('SELECT SUM(Quantity*UnitPriceCents) FROM OrderItems').fetchone()[0]
    source = json.loads((root / 'Import' / 'seed.json').read_text(encoding='utf-8-sig'))
    expected = sum(int((Decimal(row['L']) * 100).quantize(Decimal('1'), rounding=ROUND_HALF_UP)) for row in source['orders'][1:])
    assert total == expected, (total, expected)
doc = Document(root / 'Documentation' / 'Verification.docx')
assert len(doc.inline_shapes) == 6
algorithm = PdfReader(root / 'Documentation' / 'Algorithm.pdf')
assert len(algorithm.pages) == 3
for page in algorithm.pages:
    assert 'Начало' in page.extract_text()
print(f'PASS: SQL restore, counts, foreign keys, integrity, source order total {total/100:.2f} RUB, six report images')
