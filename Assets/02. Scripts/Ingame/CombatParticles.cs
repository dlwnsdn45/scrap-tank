using System.Collections.Generic;
using UnityEngine;

internal static class CombatParticles
{
    static readonly Dictionary<string, ParticleSystem> prefabs = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => prefabs.Clear();

    internal static ParticleSystem GetPrefab(string name)
    {
        if (prefabs.TryGetValue(name, out var prefab)) return prefab;
        prefab = Resources.Load<ParticleSystem>("Particles/" + name);
        if (prefab) prefabs[name] = prefab;
        else Debug.LogWarning($"Particle prefab '{name}' is missing from Resources/Particles.");
        return prefab;
    }

    internal static void Play(string name, Vector2 position, Vector2 direction, Transform follow = null,
        float range = 0f, float arc = 0f)
    {
        var prefab = GetPrefab(name);
        if (!prefab) return;
        Quaternion rotation = Quaternion.LookRotation(Vector3.forward, direction == Vector2.zero ? Vector3.up : (Vector3)direction);
        var effect = Object.Instantiate(prefab, new Vector3(position.x, position.y, -0.4f), rotation, follow);
        if (name == "Flame" && range > 0f)
        {
            var main = effect.main;
            main.startLifetime = 0.6f;
            main.startSpeed = range / 0.6f;
            var shape = effect.shape;
            shape.angle = arc * 0.5f;
        }
        if (name == "EmpWave" && range > 0f)
        {
            var main = effect.main;
            main.startSpeed = range / 0.45f;
            var shape = effect.shape;
            shape.arc = arc;
            shape.rotation = new Vector3(0f, 0f, 90f - arc * 0.5f);
        }
        effect.Play(true);
    }

    internal static void Jam(Transform target)
    {
        var existing = target.Find("EmpSparks(Clone)");
        if (existing)
        {
            var particles = existing.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(true);
            return;
        }
        Play("EmpSparks", target.position, Vector2.up, target);
    }

    internal static void ClearJam(Transform target)
    {
        var existing = target.Find("EmpSparks(Clone)");
        if (existing) Object.Destroy(existing.gameObject);
    }
}

