# Aircraft assets

Original, textureless low-poly models created for Family Time Aircraft in Blender 5.2.2. No game content or third-party assets are included.

The wooden open-cabin aircraft is 12 metres across, 9.015 metres long and 3.4 metres tall. Four benches provide eight passenger positions behind one pilot. Origin is at ground level beneath the landing gear. Runtime axes are +Y up and +Z forward. `Propeller` rotates about its local Z axis. Seats are named `Pilot` and `Seat1` through `Seat8`.

The repairable wreck retains the aircraft footprint, darkens the materials, tilts the wing and fin and breaks the propeller. The runway is 80 by 16 metres with threshold, edge and centre markings. `Parking`, `RunwayStart` and `RunwayEnd` are placement markers.

## Outputs

- `aircraft.fta`, `wreck.fta`, `runway.fta`: runtime meshes requiring no Unity editor or glTF package.
- Corresponding `.glb` files: portable geometry with Principled BSDF colors.
- `aircraft_v001.blend`: editable source scene; user startup content is retained separately and excluded from exports.
- `aircraft-multiview.png`: inspected hero, top, side and wreck views.
- `aircraft-thumbnail.png`, `runway-thumbnail.png`: transparent 256x256 native building-menu thumbnails.
- `validation-report.json`, `roundtrip-report.json`: structural and portable export checks.

## FTA1 format

All numbers are little endian. Header: four ASCII bytes `FTA1`, followed by signed int32 record count. Every record is:

1. Signed int32 UTF8 name byte count, then exactly those bytes.
2. Signed int32 vertex count and signed int32 index count.
3. Vertex positions: vertex count float32 triples.
4. Vertex normals: vertex count float32 triples.
5. One RGBA float32 quadruple for the record material.
6. Triangle indices: index count signed int32 values.
7. Local translation: three float32 values.
8. Local rotation: four float32 values in quaternion x,y,z,w order.
9. Local scale: three float32 values.

Records are flat children of the asset root. Empty markers have zero vertices and zero indices, but retain their transforms. Rotations and scales are baked into mesh vertices and normals, preserving the translated pivot. Blender `(x,y,z)` converts to Unity `(x,z,y)` with reversed winding. Runtime rotations and scales are identity. All models are under 1,000 triangles each.

## Rebuild and verify

```sh
blender -b -t 4 --python tools/generate-aircraft-assets.py
python3 tools/check-aircraft-assets.py
```

Production ran in an isolated headless Blender process. The existing localhost BlenderMCP scene was inspected through its socket API before creation and left untouched. Render images were inspected visually; FTA arrays, unit normals, unique names, marker presence, scale and GLB scene isolation were checked. All three GLB exports were imported back into fresh Blender scenes and their mesh counts verified.
