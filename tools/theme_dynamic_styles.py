from pathlib import Path

p = Path("src/Luma.App/App.axaml")
c = p.read_text(encoding="utf-8")
idx = c.find("<Application.Styles>")
if idx < 0:
    raise SystemExit("no styles")
head, tail = c[:idx], c[idx:]
replacements = [
    ('Value="#7C4DFF"', 'Value="{DynamicResource AccentBrush}"'),
    ('Value="#2563EB"', 'Value="{DynamicResource AccentBrush}"'),
    ('Value="#3B82F6"', 'Value="{DynamicResource AccentSoftBrush}"'),
]
for a, b in replacements:
    tail = tail.replace(a, b)
p.write_text(head + tail, encoding="utf-8")
print("ok")
