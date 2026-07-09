from pathlib import Path

p = Path("src/Luma.App/MainWindow.axaml")
c = p.read_text(encoding="utf-8")
# Common accent usages → theme resources
pairs = [
    ('Stroke="#7C4DFF"', 'Stroke="{DynamicResource AccentBrush}"'),
    ('Fill="#7C4DFF"', 'Fill="{DynamicResource AccentBrush}"'),
    ('Foreground="#4338CA"', 'Foreground="{DynamicResource AccentSoftBrush}"'),
    ('Stroke="#4338CA"', 'Stroke="{DynamicResource AccentSoftBrush}"'),
    ('CaretBrush="#7C4DFF"', 'CaretBrush="{DynamicResource AccentBrush}"'),
    ('SelectionBrush="#332563FF"', 'SelectionBrush="#332563EB"'),
    ('PatternColor="#7C4DFF"', 'PatternColor="#2563EB"'),
    ('PatternColor="#2563FF"', 'PatternColor="#3B82F6"'),
    ('SatelliteBrush="#7C4DFF"', 'SatelliteBrush="#2563EB"'),
    ('OrbitBrush="#D92563FF"', 'OrbitBrush="#D93B82F6"'),
    ('Foreground="#080F23"', 'Foreground="{DynamicResource TextBrightBrush}"'),
    # dock ring gradient
    ('Color="#CC7C4DFF"', 'Color="#CC2563EB"'),
    ('Color="#AA00E5FF"', 'Color="#AA38BDF8"'),
    ('Stroke="#C4B5FD"', 'Stroke="#93C5FD"'),
]
for a, b in pairs:
    c = c.replace(a, b)
p.write_text(c, encoding="utf-8")
print("mainwindow ok")
