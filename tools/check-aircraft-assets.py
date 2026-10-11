import json
import math
import re
import struct
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / 'assets'


def read_vector(stream, count):
    return struct.unpack('<'+'f'*count,stream.read(count*4))


def read_integer(stream):
    return struct.unpack('<i',stream.read(4))[0]


def triangle_volume(positions, indices):
    a,b,c = (positions[i] for i in indices)
    cross = (b[1]*c[2]-b[2]*c[1],b[2]*c[0]-b[0]*c[2],b[0]*c[1]-b[1]*c[0])
    return sum(v*w for v,w in zip(a,cross))/6


def read_record(stream):
    name = stream.read(read_integer(stream)).decode('utf8')
    vertex_count,index_count = read_integer(stream),read_integer(stream)
    assert 0 <= vertex_count <= 100000 and 0 <= index_count <= 300000
    positions = [read_vector(stream,3) for _ in range(vertex_count)]
    normals = [read_vector(stream,3) for _ in range(vertex_count)]
    color = read_vector(stream,4)
    indices = [read_integer(stream) for _ in range(index_count)]
    translation = read_vector(stream,3)
    rotation,scale = read_vector(stream,4),read_vector(stream,3)
    assert len(indices)%3 == 0 and all(0 <= i < vertex_count for i in indices)
    if positions:
        signed_volume = sum(triangle_volume(positions, indices[i:i+3])
                            for i in range(0,len(indices),3))
        assert signed_volume > 0, 'Inward winding on ' + name
    values = [v for p in positions+normals for v in p]
    assert all(math.isfinite(v) for v in values+list(color)+list(translation))
    assert all(0 <= v <= 1 for v in color)
    assert rotation == (0,0,0,1) and scale == (1,1,1)
    assert all(abs(sum(v*v for v in n)-1) < 0.001 for n in normals)
    world = [tuple(a+b for a,b in zip(p,translation)) for p in positions]
    return {'name':name,'positions':world,'triangles':len(indices)//3,'translation':translation}


def inspect_fta(name):
    with (ASSETS/(name+'.fta')).open('rb') as stream:
        assert stream.read(4) == b'FTA1'
        records = [read_record(stream) for _ in range(read_integer(stream))]
        assert not stream.read(1),'Unexpected trailing bytes'
    names = [r['name'] for r in records]
    assert len(names) == len(set(names))
    vertices = [p for r in records for p in r['positions']]
    bounds = [[min(p[a] for p in vertices),max(p[a] for p in vertices)] for a in range(3)]
    if name == 'aircraft':
        assert all(n in names for n in ['Pilot','Propeller']+['Seat'+str(i) for i in range(1,9)])
        assert abs(bounds[0][1]-bounds[0][0]-12) < 0.01
        assert abs(bounds[1][0]) < 0.01
        assert 8.5 < bounds[2][1]-bounds[2][0] < 9.5
    if name == 'runway':
        assert abs(bounds[0][1]-bounds[0][0]-16) < 0.01
        assert abs(bounds[2][1]-bounds[2][0]-80) < 0.01
    return {'records':len(records),'triangles':sum(r['triangles'] for r in records),'boundsUnity':bounds,'names':names}


def inspect_glb(name):
    blob = (ASSETS/(name+'.glb')).read_bytes()
    magic,version,length = struct.unpack_from('<4sII',blob)
    assert magic == b'glTF' and version == 2 and length == len(blob)
    chunk_size,chunk_type = struct.unpack_from('<II',blob,12)
    assert chunk_type == 0x4E4F534A
    data = json.loads(blob[20:20+chunk_size])
    assert len(data['scenes']) == 1
    names = [node.get('name','') for node in data['nodes']]
    assert 'Cube' not in names
    assert all('GEO-' in mesh['name'] for mesh in data['meshes'])
    return {'meshes':len(data['meshes']),'bytes':len(blob)}


def validate_tower_section_names(names):
    required = {f'TowerSection_{tower}_{level}_{part}'
                for tower in range(2) for level in range(8) for part in ('Body','Windows')}
    present = {name for name in names if re.fullmatch(r'TowerSection_[01]_[0-7]_(Body|Windows)', name)}
    missing = sorted(required - present)
    if missing:
        raise AssertionError('Missing tower section parts: ' + ', '.join(missing))
    if len(present) != len(required):
        raise AssertionError('Tower section parts are duplicated.')


def inspect_tower_assets():
    pair = inspect_fta('tower-pair')
    debris = inspect_fta('tower-debris')
    validate_tower_section_names(pair['names'])
    assert debris['records'] == 6, 'Tower debris set must contain six bounded pieces.'
    return {'towerPair':pair, 'towerDebris':debris, 'glb':inspect_glb('tower-pair')}


def main():
    results = {name:{'fta':inspect_fta(name),'glb':inspect_glb(name)}
               for name in ['aircraft','wreck','runway']}
    results['towers'] = inspect_tower_assets()
    for filename in ['aircraft_v001.blend','aircraft-hero.png','aircraft-front.png',
                     'aircraft-side.png','aircraft-top.png','wreck-hero.png',
                     'runway-hero.png','aircraft-thumbnail.png','runway-thumbnail.png',
                     'tower-pair_v001.blend','tower-pair-thumbnail.png','tower-pair-preview.png']:
        assert (ASSETS/filename).stat().st_size > 0
    (ASSETS/'validation-report.json').write_text(json.dumps(results,indent=2)+'\n')
    print(json.dumps(results,indent=2))


if __name__ == '__main__':
    main()
