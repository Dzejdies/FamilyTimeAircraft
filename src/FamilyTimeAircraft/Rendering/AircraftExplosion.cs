using System;
using System.Collections.Generic;
using UnityEngine;

namespace FamilyTimeAircraft.Rendering;

public sealed class AircraftExplosion : MonoBehaviour
{
    private const float EffectLifetime = 20;
    private const int SoundSamples = 44100;
    private const float FireballSize = 7;
    private const float FireballEmission = 8;
    private const float SparkEmission = 4;
    private const float FireballDuration = 1.2f;
    private const int FireballLatitudeSegments = 8;
    private const int FireballLongitudeSegments = 12;
    private readonly List<Material> _materials = new List<Material>();
    private readonly List<Mesh> _meshes = new List<Mesh>();
    private readonly List<FireballBlob> _fireballBlobs = new List<FireballBlob>();
    private float _fireballStartedAt;

    private sealed class FireballBlob
    {
        public Transform Transform = null!;
        public Material Material = null!;
        public Vector3 Direction;
        public Color Color;
        public float Diameter;
    }
    private AudioClip? _clip;
    private Texture2D? _particleTexture;

    public static void Play(Vector3 position)
    {
        var effect = new GameObject("Aircraft explosion");
        effect.transform.position = position;
        try
        {
            effect.AddComponent<AircraftExplosion>().Initialize();
            Destroy(effect, EffectLifetime);
        }
        catch
        {
            Destroy(effect);
            throw;
        }
    }

    private void Initialize()
    {
        CreateParticles("Fireball", new Color(1, 0.65f, 0.12f), 100, 9, 2.5f, FireballSize, 0);
        CreateParticles("Smoke", new Color(0.18f, 0.15f, 0.12f, 0.65f), 90, 5, 16, 5, 1.2f);
        CreateParticles("Sparks", new Color(1, 0.8f, 0.3f), 90, 22, 3, 0.18f, 0);
        CreateFireballCore();
        PlaySound();
        var flash = gameObject.AddComponent<Light>();
        flash.color = new Color(1, 0.45f, 0.08f);
        flash.range = 55;
        flash.intensity = 16;
        Destroy(flash, 0.25f);
    }

    private void CreateParticles(string name, Color color, int count, float speed, float lifetime, float size, float delay)
    {
        var effect = new GameObject(name);
        effect.transform.SetParent(transform, false);
        ParticleSystem particles = effect.AddComponent<ParticleSystem>();
        particles.Stop();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.duration = 0.5f;
        main.startDelay = delay;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = name == "Smoke" ? -0.12f : 0.2f;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = name == "Fireball" ? 3 : 1.5f;
        SetParticleMaterial(particles, color, name);
        ConfigureExpansion(particles, name);
        particles.Play();
    }

    private static void ConfigureExpansion(ParticleSystem particles, string name)
    {
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, name == "Fireball"
            ? new AnimationCurve(new Keyframe(0, 0.45f), new Keyframe(0.2f, 1.4f), new Keyframe(1, 0.5f))
            : new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(1, 1.8f)));
    }

    private void SetParticleMaterial(ParticleSystem particles, Color color, string name)
    {
        var material = new Material(FindParticleShader());
        _materials.Add(material);
        ConfigureTransparentParticles(material, name != "Smoke");
        material.color = color;
        material.SetColor("_BaseColor", color);
        float emission = name == "Fireball" ? FireballEmission : name == "Sparks" ? SparkEmission : 0;
        if (emission > 0) ConfigureEmission(material, color * emission);
        particles.GetComponent<ParticleSystemRenderer>().material = material;
    }

    private static Shader FindParticleShader() => FindSupportedShader(new[]
    {
        "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit",
        "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit"
    });

    private void ConfigureEmission(Material material, Color emission)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        material.SetTexture("_EmissionMap", _particleTexture);
    }

    private void ConfigureTransparentParticles(Material material, bool additive)
    {
        _particleTexture ??= CreateParticleTexture();
        material.SetTexture("_BaseMap", _particleTexture);
        material.SetTexture("_MainTex", _particleTexture);
        material.SetFloat("_Surface", 1);
        material.SetFloat("_Blend", additive ? 2 : 0);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static Texture2D CreateParticleTexture()
    {
        const int textureSize = 64;
        var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < textureSize; y++)
            for (int x = 0; x < textureSize; x++)
            {
                float radius = new Vector2((x + 0.5f) / textureSize * 2 - 1, (y + 0.5f) / textureSize * 2 - 1).magnitude;
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(1, 0, Mathf.Clamp01(radius))));
            }
        texture.Apply();
        return texture;
    }

    private void CreateFireballCore()
    {
        Mesh mesh = CreateFireballMesh();
        _fireballStartedAt = Time.time;
        CreateFireballBlob(mesh, Vector3.zero, 12, new Color(1, 0.8f, 0.15f));
        CreateFireballBlob(mesh, new Vector3(-1, 0.25f, 0.2f), 10, new Color(1, 0.22f, 0.015f));
        CreateFireballBlob(mesh, new Vector3(1, 0.45f, -0.3f), 9, new Color(1, 0.4f, 0.025f));
        CreateFireballBlob(mesh, new Vector3(0.3f, 1, 0.3f), 10, new Color(1, 0.65f, 0.06f));
        CreateFireballBlob(mesh, new Vector3(-0.2f, -0.25f, 1), 9, new Color(1, 0.3f, 0.015f));
        CreateFireballBlob(mesh, new Vector3(0.1f, 0.2f, -1), 10, new Color(1, 0.45f, 0.025f));
    }

    private void CreateFireballBlob(Mesh mesh, Vector3 direction, float diameter, Color color)
    {
        var blob = new GameObject("Fireball core");
        blob.transform.SetParent(transform, false);
        blob.transform.localScale = Vector3.zero;
        blob.AddComponent<MeshFilter>().sharedMesh = mesh;
        Material material = CreateFireballMaterial(color);
        MeshRenderer renderer = blob.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        _fireballBlobs.Add(new FireballBlob { Transform = blob.transform, Material = material, Direction = direction, Diameter = diameter, Color = color });
    }

    private Material CreateFireballMaterial(Color color)
    {
        Shader shader = FindSupportedShader(new[] { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit" });
        var material = new Material(shader);
        _materials.Add(material);
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 4);
        return material;
    }

    private Mesh CreateFireballMesh()
    {
        var mesh = new Mesh { name = "Aircraft fireball sphere" };
        _meshes.Add(mesh);
        Vector3[] vertices = FireballVertices();
        var normals = new Vector3[vertices.Length];
        for (int index = 0; index < vertices.Length; index++) normals[index] = vertices[index].normalized;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = FireballTriangles();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3[] FireballVertices()
    {
        var vertices = new Vector3[(FireballLatitudeSegments + 1) * FireballLongitudeSegments];
        for (int latitude = 0; latitude <= FireballLatitudeSegments; latitude++)
            for (int longitude = 0; longitude < FireballLongitudeSegments; longitude++)
            {
                float verticalAngle = Mathf.PI * latitude / FireballLatitudeSegments;
                float horizontalAngle = Mathf.PI * 2 * longitude / FireballLongitudeSegments;
                vertices[latitude * FireballLongitudeSegments + longitude] = new Vector3(Mathf.Sin(verticalAngle) * Mathf.Cos(horizontalAngle), Mathf.Cos(verticalAngle), Mathf.Sin(verticalAngle) * Mathf.Sin(horizontalAngle)) * 0.5f;
            }
        return vertices;
    }

    private static int[] FireballTriangles()
    {
        var triangles = new int[FireballLatitudeSegments * FireballLongitudeSegments * 6];
        int offset = 0;
        for (int latitude = 0; latitude < FireballLatitudeSegments; latitude++)
            for (int longitude = 0; longitude < FireballLongitudeSegments; longitude++)
            {
                int first = latitude * FireballLongitudeSegments + longitude;
                int next = latitude * FireballLongitudeSegments + (longitude + 1) % FireballLongitudeSegments;
                foreach (int vertex in new[] { first, next, first + FireballLongitudeSegments, next, next + FireballLongitudeSegments, first + FireballLongitudeSegments }) triangles[offset++] = vertex;
            }
        return triangles;
    }

    private void Update()
    {
        if (_fireballBlobs.Count == 0) return;
        float progress = (Time.time - _fireballStartedAt) / FireballDuration;
        if (progress >= 1) { RemoveFireballCore(); return; }
        float expansion = Mathf.SmoothStep(0, 1, progress / 0.18f) * Mathf.SmoothStep(1, 0, Mathf.Clamp01((progress - 0.6f) / 0.4f));
        foreach (FireballBlob blob in _fireballBlobs)
        {
            blob.Transform.localPosition = blob.Direction * (1 + progress * 5);
            blob.Transform.localScale = Vector3.one * blob.Diameter * expansion;
            blob.Material.SetColor("_EmissionColor", blob.Color * Mathf.Lerp(4, 0.5f, progress));
        }
    }

    private void RemoveFireballCore()
    {
        foreach (FireballBlob blob in _fireballBlobs) Destroy(blob.Transform.gameObject);
        _fireballBlobs.Clear();
    }

    private static Shader FindSupportedShader(string[] names)
    {
        foreach (string name in names)
        {
            Shader shader = Shader.Find(name);
            if (shader != null && shader.isSupported) return shader;
        }
        throw new InvalidOperationException("Aircraft explosion shader is unavailable.");
    }

    private void PlaySound()
    {
        var random = new System.Random(91);
        float[] samples = new float[SoundSamples * 2];
        for (int index = 0; index < samples.Length; index++)
        {
            float time = (float)index / SoundSamples;
            double envelope = Math.Exp(-time * 3.5);
            samples[index] = (float)((random.NextDouble() * 2 - 1) * envelope * 0.55 + Math.Sin(time * 95) * envelope * 0.35);
        }
        _clip = AudioClip.Create("Aircraft impact", samples.Length, 1, SoundSamples, false);
        _clip.SetData(samples, 0);
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = _clip;
        source.spatialBlend = 1;
        source.minDistance = 20;
        source.maxDistance = 300;
        source.Play();
    }

    private void OnDestroy()
    {
        if (_clip != null) Destroy(_clip);
        if (_particleTexture != null) Destroy(_particleTexture);
        foreach (Mesh mesh in _meshes) Destroy(mesh);
        _meshes.Clear();
        foreach (Material material in _materials) Destroy(material);
        _materials.Clear();
    }
}
