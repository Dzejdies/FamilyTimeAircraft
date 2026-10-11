import io
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def vector(stream, count=3):
    return struct.unpack('<' + 'f' * count, stream.read(count * 4))


def subtract(first, second):
    return tuple(a - b for a, b in zip(first, second))


def cross(first, second):
    return (first[1] * second[2] - first[2] * second[1], first[2] * second[0] - first[0] * second[2],
            first[0] * second[1] - first[1] * second[0])


def dot(first, second):
    return sum(a * b for a, b in zip(first, second))


def read_record(stream):
    name_length = struct.unpack('<i', stream.read(4))[0]
    assert 0 < name_length <= 256
    name = stream.read(name_length).decode('utf8')
    vertex_count, index_count = struct.unpack('<ii', stream.read(8))
    assert 0 <= vertex_count <= 100000 and 0 <= index_count <= 300000 and index_count % 3 == 0
    positions = [vector(stream) for _ in range(vertex_count)]
    normals = [vector(stream) for _ in range(vertex_count)]
    color = vector(stream, 4)
    indices = struct.unpack('<' + 'i' * index_count, stream.read(index_count * 4))
    translation, rotation, scale = vector(stream), vector(stream, 4), vector(stream)
    assert all(math.isfinite(value) for values in positions + normals + [color, translation, rotation, scale] for value in values)
    assert rotation == (0, 0, 0, 1) and scale == (1, 1, 1)
    return name, positions, normals, indices, translation


def validate_record(record):
    name, positions, normals, indices, translation = record
    assert all(0 <= index < len(positions) for index in indices)
    assert all(abs(dot(normal, normal) - 1) < 0.0001 for normal in normals)
    volume = 0
    for start in range(0, len(indices), 3):
        a, b, c = (positions[index] for index in indices[start:start + 3])
        face = cross(subtract(b, a), subtract(c, a))
        assert dot(face, face) > 1e-14, name
        assert dot(face, normals[indices[start]]) > 0, name
        volume += dot(a, cross(b, c)) / 6
    assert not positions or volume > 0, (name, volume)
    return [tuple(point[axis] + translation[axis] for axis in range(3)) for point in positions]


def validate_fta(path):
    stream = io.BytesIO(path.read_bytes())
    assert stream.read(4) == b'FTA1'
    count = struct.unpack('<i', stream.read(4))[0]
    assert count == 49
    records = [read_record(stream) for _ in range(count)]
    assert stream.tell() == len(stream.getvalue())
    assert len({record[0] for record in records}) == count
    points = [point for record in records for point in validate_record(record)]
    minimum = [min(point[axis] for point in points) for axis in range(3)]
    maximum = [max(point[axis] for point in points) for axis in range(3)]
    assert abs(maximum[0] - minimum[0] - 3.6) < 0.001
    assert abs(maximum[1] - 3.65) < 0.001
    assert any(name == 'OccupantCenter' and translation == (0, 0, 0) for name, _, _, _, translation in records)
    return {'records': count, 'triangles': sum(len(record[3]) // 3 for record in records), 'minimum': minimum, 'maximum': maximum}


def validate_glb(path):
    payload = path.read_bytes()
    magic, version, length = struct.unpack('<4sII', payload[:12])
    assert magic == b'glTF' and version == 2 and length == len(payload)
    json_length, kind = struct.unpack('<I4s', payload[12:20])
    assert kind == b'JSON'
    document = json.loads(payload[20:20 + json_length])
    assert len(document['meshes']) == 48
    assert all('uri' not in buffer for buffer in document['buffers'])
    triangles = sum(document['accessors'][primitive['indices']]['count'] // 3
                    for mesh in document['meshes'] for primitive in mesh['primitives'])
    assert triangles == 1088
    return {'meshes': len(document['meshes']), 'triangles': triangles, 'bytes': len(payload)}


def main():
    result = {'fta': validate_fta(ROOT / 'assets/parachute.fta'), 'glb': validate_glb(ROOT / 'assets/parachute.glb')}
    for name in ['parachute_v001.blend', 'parachute-preview.png', 'parachute-thumbnail.png']:
        assert (ROOT / 'assets' / name).stat().st_size > 0
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
