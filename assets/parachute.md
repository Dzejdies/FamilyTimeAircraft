# Parachute model contract

The runtime asset is `parachute.fta`, exported with the existing FTA1 exporter. It uses metres, Unity +Y up and +Z forward, baked rotation/scale and a flat record hierarchy. `OccupantCenter` is a zero-geometry marker at the origin.

The origin is the occupant's torso/harness centre. A native character foot anchor requires a separate runtime attachment offset; the model does not contain that offset or a character.

The canopy has a 3.6 m diameter, a 2.9 m rim height and a 3.65 m crown height relative to the origin. Sixteen closed fabric gores provide outward-facing upper and lower surfaces. Sixteen suspension lines connect the rim to four risers, which connect to the harness. The harness extends approximately -0.423..0.55 m vertically. There are 48 mesh records, one marker and 1,088 triangles.

`parachute.glb` is the portable, material-preserving export; glTF uses its standard right-handed Y-up convention. `parachute_v001.blend` is the editable source with the owned `FamilyTimeParachute_v001` scene. No aircraft, wreck or runway assets were regenerated.

Run `python3 tools/validate-parachute-assets.py` to check the binary schema, indices, finite values, unit normals, winding alignment, positive mesh volumes, bounds and GLB counts. The GLB was also reimported into a separate Blender scene and retained 48 meshes, 1,088 triangles and the expected metre dimensions. The hero, front and underside renders were inspected; root accepted the hero preview.
