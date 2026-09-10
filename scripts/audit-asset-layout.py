"""Verify the asset reorganization without changing files, images, or databases."""
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def main():
    inventory = json.loads((ROOT / 'docs/assets/reorganization-20260908.json').read_text())['images']
    expected = {item['after'] for item in inventory}
    assert len(expected) == len(inventory), 'Overlapping destinations'
    moved = 0
    for item in inventory:
        path = ROOT / item['after']
        assert path.is_file(), 'Missing: ' + item['after']
        assert hashlib.sha256(path.read_bytes()).hexdigest() == item['sha256'], 'Image bytes changed: ' + item['after']
        if item['before'] != item['after']:
            assert not (ROOT / item['before']).exists(), 'Old copy remains: ' + item['before']
            moved += 1
    actual = {path.relative_to(ROOT).as_posix() for directory in (
        'frontend/assets/pet', 'frontend/previews', 'docs/screenshots')
        for path in (ROOT / directory).rglob('*.png')}
    assert actual == expected, {'unexpected': sorted(actual - expected), 'missing': sorted(expected - actual)}

    # Publish selection must contain only the web derivatives, transparent atlases,
    # cat cutouts and individual reward sprites. Keep runtime URLs unchanged.
    project = ET.parse(ROOT / 'TaskApi.csproj').getroot()
    published = set()
    for content in project.iter('Content'):
        for pattern in content.get('Include', '').split(';'):
            if pattern:
                published.update(path.relative_to(ROOT).as_posix() for path in ROOT.glob(pattern) if path.suffix == '.png')
    assert len(published) == 111, len(published)
    for category in ['sources', 'drafts', 'archive']:
        assert not any('/' + category + '/' in path for path in published), category
        assert 'frontend/assets/pet/' + category in (ROOT / '.dockerignore').read_text().splitlines()
    assert all(any(item['before'] == path and item['after'] == path for item in inventory) for path in published)

    catalog_path = ROOT / 'frontend/previews/pet-outfits-v1/catalog.js'
    catalog = json.loads(catalog_path.read_text().split('const OutfitCatalog = ', 1)[1].rstrip().removesuffix(';'))
    assert len(catalog) == 90
    for item in catalog:
        path = (catalog_path.parent / item['src']).resolve()
        assert path.is_relative_to(ROOT / 'frontend/assets/pet/drafts/outfits-v1'), item['key']
        assert path.is_file(), item['src']
    jobs = json.loads((ROOT / 'docs/pet-outfits-v1/manifest.json').read_text())['jobs']
    for job in jobs:
        assert (ROOT / job['asset']).is_file(), job['asset']
        result = json.loads((ROOT / 'docs/pet-outfits-v1/results' / (job['key'] + '.json')).read_text())
        assert job['asset'] == result['asset']
        for reference in job['references']:
            # Generation metadata records absolute paths; resolve the workspace
            # suffix for CI/checkouts on a different machine.
            relative = reference.split('/Applications/task-app/', 1)[1]
            assert (ROOT / relative).is_file(), relative

    readme = (ROOT / 'README.md').read_text()
    links = re.findall(r'(?:src="|\]\()([^\s"()]+\.png)', readme)
    for link in links:
        if not link.startswith(('http:', 'https:')):
            assert (ROOT / link).is_file(), 'Broken README image: ' + link
    guide_links = 0
    for relative in (
        'docs/assets/README.md', 'docs/screenshots/README.md',
        'frontend/assets/pet/README.md', 'frontend/assets/pet/sources/README.md',
        'frontend/assets/pet/drafts/README.md', 'frontend/assets/pet/archive/README.md',
        'frontend/previews/README.md',
    ):
        guide = ROOT / relative
        for link in re.findall(r'\]\(([^\s()]+)\)', guide.read_text()):
            if not link.startswith(('http:', 'https:', '#')):
                assert (guide.parent / link.split('#', 1)[0]).exists(), 'Broken guide link: ' + relative + ': ' + link
                guide_links += 1
    print(json.dumps({'imagesVerified': len(inventory), 'movedWithoutChanges': moved,
                      'runtimeImagesUnchanged': len(published), 'outfitPreviews': len(catalog),
                      'readmeImageLinks': len(links), 'guideLinks': guide_links,
                      'deletedImages': 0}), flush=True)


if __name__ == '__main__':
    main()
