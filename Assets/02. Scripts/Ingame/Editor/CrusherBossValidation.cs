#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Runs against real Unity physics and the existing Ingame scene, without saving scene changes.
[InitializeOnLoad]
public static class CrusherBossValidation
{
    const string Pending = "CrusherBossValidation.Pending";
    const string ExitCode = "CrusherBossValidation.ExitCode";
    const string OriginalPlayOptions = "CrusherBossValidation.OriginalPlayOptions";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int frames, assertions;

    static CrusherBossValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayStateChanged;
    }

    [MenuItem("Tools/Scrap Tank/Validate Crusher Boss")]
    public static void Run()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetInt(OriginalPlayOptions, (int)EditorSettings.enterPlayModeOptions);
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
        SessionState.SetBool(Pending, true);
        SessionState.SetInt(ExitCode, 1);
        if (EditorApplication.isPlaying) EditorApplication.update += WaitForScene;
        else
        {
            EditorSceneManager.OpenScene("Assets/00. Scenes/Ingame.unity");
            EditorApplication.isPlaying = true;
        }
    }

    static void OnPlayStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            frames = 0;
            EditorApplication.update -= WaitForScene;
            EditorApplication.update += WaitForScene;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OriginalPlayOptions, 0);
            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(ExitCode, 1));
        }
    }

    static void WaitForScene()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || ++frames < 20) return;
        EditorApplication.update -= WaitForScene;
        try
        {
            CheckCombat();
            Debug.Log($"CRUSHER VALIDATION PASSED ({assertions} assertions)");
            SessionState.SetInt(ExitCode, 0);
        }
        catch (Exception error)
        {
            Debug.LogError("CRUSHER VALIDATION FAILED: " + error);
            SessionState.SetInt(ExitCode, 1);
        }
        finally { EditorApplication.isPlaying = false; }
    }

    static void CheckCombat()
    {
        var encounter = BossEncounterController.Instance;
        Require(encounter, "Encounter automatically attaches in the Ingame scene");
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        Require(player && player.IsAlive, "Live player exists");
        Time.timeScale = 1f;
        CheckUiTeardownAndPausedCamera(player);
        var manager = UnityEngine.Object.FindAnyObjectByType<EnemyManager>();
        CheckRepairPauseAndDamage(player);
        CheckDifficulty(manager);
        CheckSpawning(manager);
        // Keep unrelated mob damage from affecting the deterministic checks.
        foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>())
            enemy.gameObject.SetActive(false);
        CheckMissileHoming(manager, player);
        encounter.SpawnBoss();
        int enemiesAtBossSpawn = Get<System.Collections.Generic.List<Enemy>>(manager, "enemies").Count;
        Call(manager, "TickSpawning", 100f);
        Require(Get<System.Collections.Generic.List<Enemy>>(manager, "enemies").Count == enemiesAtBossSpawn,
            "Boss arrival stops new mob waves without deleting existing enemies");
        var boss = encounter.Boss;
        Require(boss && boss.GetComponentInChildren<SpriteRenderer>(), "Fallback boss created with visible geometry");
        int hp = boss.Hp;
        boss.TakeDamage(10);
        Require(boss.Hp == hp && !boss.IsTargetable, "Intro is invulnerable and untargetable");
        Call(boss, "EnterState", CrusherBossController.BossState.Select);
        Call(boss, "SelectPattern");
        Require(boss.State == CrusherBossController.BossState.ChargeReady, "First pattern is charge");
        var previous = Get<CrusherBossController.Pattern>(boss, "lastPattern");
        for (int i = 0; i < 200; i++)
        {
            Call(boss, "SelectPattern");
            var next = Get<CrusherBossController.Pattern>(boss, "lastPattern");
            Require(next != previous, "Consecutive patterns differ");
            previous = next;
        }

        MovePlayer(player, encounter.Arena.min + Vector2.one * 4f);
        Set(boss, "repetitions", 0);
        Set(boss, "chargeDistance", .1f);
        Call(boss, "EnterState", CrusherBossController.BossState.Charge);
        Call(boss, "TickCharge");
        Require(boss.State == CrusherBossController.BossState.ChargeWait, "First charge waits");
        Call(boss, "EnterState", CrusherBossController.BossState.Charge);
        Call(boss, "TickCharge");
        Require(boss.State == CrusherBossController.BossState.Delay, "Second charge finishes");

        // Actual BoxCast must hit a player between the start and end, once per charge.
        boss.GetComponent<Rigidbody2D>().position = encounter.Arena.center;
        boss.GetComponent<Rigidbody2D>().rotation = 0f;
        MovePlayer(player, encounter.Arena.center + Vector2.up * 3f);
        Set(boss, "chargeDistance", 10f);
        Set(boss, "chargeSpeed", 250f);
        Call(boss, "EnterState", CrusherBossController.BossState.Charge);
        uint damageVersion = Get<uint>(player, "DamageVersion");
        Call(boss, "TickCharge");
        Require(Get<uint>(player, "DamageVersion") == damageVersion + 1, "Swept charge hits across a long physics step");
        damageVersion = Get<uint>(player, "DamageVersion");
        Call(boss, "TickCharge");
        Require(Get<uint>(player, "DamageVersion") == damageVersion, "One charge does not damage twice");
        Set(player, "hp", player.MaxHp);

        CheckEruption(boss, player, false);
        CheckEruption(boss, player, true);

        Call(boss, "EnterState", CrusherBossController.BossState.Delay);
        Vector2 firingPoint = (Vector2)boss.transform.position - Vector2.right * 6f;
        foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
        {
            var weapon = new WeaponDefinition { kind = kind, damage = 1, range = 12f, arc = 180f, projectileSpeed = 30f };
            Require(ReferenceEquals(Call(manager, "FindWeaponTarget", firingPoint, Vector2.right, weapon), boss),
                "Weapon targeting includes the boss: " + kind);
            hp = boss.Hp;
            if (kind == WeaponKind.Mine)
                Call(manager, "TryDetonateMine", boss.AimPoint(firingPoint), weapon.damage, false);
            else
            {
                Call(manager, "Attack", firingPoint, Vector2.right, Vector2.right, weapon, false);
                Call(Get<object>(manager, "Projectiles"), "Tick", .25f, (LayerMask)0);
            }
            Require(boss.Hp < hp, "Weapon damages the boss: " + kind);
        }
        hp = boss.Hp;
        manager.Ram(firingPoint, (Vector2)boss.transform.position + Vector2.right * 6f, Vector2.right,
            1f, 1, new System.Collections.Generic.HashSet<CombatTarget>());
        Require(boss.Hp < hp, "Player boost ram also damages boss");

        Call(boss, "EnterState", CrusherBossController.BossState.Groggy);
        Vector2 normal = boss.WeakSide switch
        {
            CrusherBossController.ArmorSide.Front => boss.transform.up,
            CrusherBossController.ArmorSide.Back => -boss.transform.up,
            CrusherBossController.ArmorSide.Right => boss.transform.right,
            _ => -boss.transform.right
        };
        Vector2 center = boss.transform.position;
        hp = boss.Hp;
        boss.TakeDamage(new DamageHit(10, center + normal * 10f, center + normal * 2f));
        Require(hp - boss.Hp == 20, "Exposed armor doubles directional damage");
        hp = boss.Hp;
        boss.TakeDamage(new DamageHit(10, center - normal * 10f, center - normal * 2f));
        Require(hp - boss.Hp == 10, "Other armor takes ordinary damage");

        Call(boss, "EnterState", CrusherBossController.BossState.Suction);
        Vector2 suction = boss.SuctionVelocity(center + (Vector2)boss.transform.up * 5f);
        Require(suction.magnitude > Get<float>(player, "MoveSpeed") && suction.magnitude < Get<float>(player, "BoostSpeed"),
            "Suction beats normal driving but permits boosting out");
        Require(boss.SuctionVelocity(center + (Vector2)boss.transform.right * 5f) == Vector2.zero,
            "Suction immediately releases outside its cone");
        Call(boss, "EnterState", CrusherBossController.BossState.Delay);
        Require(boss.SuctionVelocity(center + (Vector2)boss.transform.up * 5f) == Vector2.zero,
            "Suction stops when state ends");

        boss.TakeDamage(boss.Hp - boss.MaxHp / 2);
        Require(boss.PhaseTwo && boss.State == CrusherBossController.BossState.Delay, "Half health changes phase without interrupting state");
        int charges = 0;
        for (int i = 0; i < 2000; i++)
        {
            Set(boss, "lastPattern", CrusherBossController.Pattern.Suction);
            Call(boss, "SelectPattern");
            if (Get<CrusherBossController.Pattern>(boss, "lastPattern") == CrusherBossController.Pattern.Charge) charges++;
        }
        Require(charges > 1240 && charges < 1560, "Phase two suction chooses charge about 70 percent of the time");
        MovePlayer(player, encounter.Arena.min + Vector2.one * 4f);
        Call(boss, "EnterState", CrusherBossController.BossState.Delay);
        Set(boss, "stateElapsed", Get<float>(boss, "delayDuration") * .75f);
        Call(boss, "FixedUpdate");
        Require(boss.State == CrusherBossController.BossState.Select, "Phase two reduces delay to 75 percent");

        Call(boss, "BuildEruptionAreas");
        Call(boss, "EnterState", CrusherBossController.BossState.EruptReady);
        boss.TakeDamage(int.MaxValue);
        Require(boss.State == CrusherBossController.BossState.Dead && boss.EruptionAreas.Count == 0 &&
            !boss.GetComponent<Collider2D>().enabled, "Death cancels telegraphs, hitbox and all attacks immediately");
        int destroyed = 0;
        boss.DestructionFinished += _ => destroyed++;
        Set(boss, "stateElapsed", 10f);
        Call(boss, "FixedUpdate");
        Call(boss, "FixedUpdate");
        Require(destroyed == 1 && encounter.ExitOpen, "Destruction opens exit exactly once");
        Require(UnityEngine.Object.FindAnyObjectByType<EnemyManager>().enabled, "Mob manager stays enabled after exit opens");
        Call(manager, "TickSpawning", 100f);
        Require(Get<System.Collections.Generic.List<Enemy>>(manager, "enemies").Count == enemiesAtBossSpawn,
            "Mob waves remain stopped after the boss dies and the exit opens");

        MovePlayer(player, encounter.ExitPosition);
        Set(GameTimeManager.Instance, "ElapsedSeconds", 480f);
        encounter.TryEscape();
        Require(encounter.Result == BossEncounterController.EncounterResult.Defeat && Time.timeScale == 0f,
            "Deadline wins over simultaneous exit overlap and freezes the game");
        UnityEngine.Object.DestroyImmediate(encounter);
        Set(player, "encounterFinished", false);
        Time.timeScale = 1f;
        Set(GameTimeManager.Instance, "ElapsedSeconds", 479f);
        var second = new GameObject("Validation Encounter").AddComponent<BossEncounterController>();
        Call(second, "Start");
        Call(second, "OpenExit", boss);
        MovePlayer(player, second.ExitPosition);
        second.TryEscape();
        Require(second.Result == BossEncounterController.EncounterResult.Victory && Time.timeScale == 0f,
            "Entering the open exit before the deadline wins");
        Debug.Log("CRUSHER CHECKS: weapons, two charges, A/B eruption, groggy, directional weakness, suction, phase two, death, exit and timeout");
    }

    static void CheckRepairPauseAndDamage(PlayerController player)
    {
        var layout = Get<object>(player, "Layout");
        var mounts = Get<WeaponMount[]>(layout, "Mounts");
        var original = (WeaponMount[])mounts.Clone();
        int oldScrap = player.Scrap, oldHp = player.Hp;
        uint oldVersion = Get<uint>(player, "DamageVersion");
        try
        {
            Array.Clear(mounts, 0, mounts.Length);
            var part = new WeaponMount();
            part.Initialize();
            mounts[0] = part;
            Vector2 source = (Vector2)player.transform.position + (Vector2)player.transform.up * 10f;
            int protectedDamage = (int)Call(layout, "AbsorbDamage", 10, source);
            Require(protectedDamage == 3, "Working armor transfers 30 percent damage");
            part.Damage(part.MaxHp);
            Require((int)Call(layout, "AbsorbDamage", 10, source) == 10,
                "Broken armor transfers full damage on later hits");

            Set(player, "Scrap", 0);
            Call(player, "BeginFieldRepair");
            Require(!Get<bool>(player, "IsFieldRepairing"), "Repair cannot begin without scrap");
            int cost = Get<int>(player, "FieldRepairCost");
            player.AddScrap(cost * 3);
            Call(player, "BeginFieldRepair");
            Require(Get<bool>(player, "IsFieldRepairing") && player.Scrap == cost * 3,
                "Repair begins without charging up front");
            Call(player, "FixedUpdate");
            Require(player.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "Repair locks movement");
            Call(player, "AdvanceFieldRepair", true, .5f);
            Call(player, "AdvanceFieldRepair", false, 0f);
            Require(part.Hp == 0 && player.Scrap == cost * 3, "Releasing R cancels without spending scrap");
            Call(player, "BeginFieldRepair");
            Call(player, "AdvanceFieldRepair", true, Get<float>(player, "fieldRepairDuration"));
            Require(part.Hp > 0 && player.Scrap == cost * 2 && !Get<bool>(player, "IsFieldRepairing"),
                "Completing repair revives exactly one part and charges once");
            part.Damage(part.MaxHp);
            Call(player, "BeginFieldRepair");
            player.TakeDamage(1, source);
            Call(player, "AdvanceFieldRepair", true, .1f);
            Require(!Get<bool>(player, "IsFieldRepairing") && part.Hp == 0 && player.Scrap == cost * 2,
                "Incoming damage interrupts repair without charge");
            Call(player, "BeginFieldRepair");
            Call(player, "SetPauseOpen", true);
            Require(Get<bool>(player, "PauseOpen") && !Get<bool>(player, "IsFieldRepairing") && Time.timeScale == 0f,
                "Pause cancels repair and freezes time");
            Call(player, "SetPauseOpen", false);
            Require(Time.timeScale == 1f, "Closing pause restores normal time");
            Call(player, "SetAssemblyOpen", true);
            Call(player, "SetPauseOpen", true);
            Require(!Get<bool>(player, "AssemblyOpen"), "Assembly and pause cannot own the pause clock together");
            Call(player, "SetPauseOpen", false);
            Require(Time.timeScale == 1f, "Pause after assembly does not leave time frozen");
        }
        finally
        {
            Call(player, "SetPauseOpen", false);
            Call(player, "StopFieldRepair");
            Array.Copy(original, mounts, mounts.Length);
            for (int i = 0; i < mounts.Length; i++) Call(layout, "RefreshVisual", i);
            Set(player, "Scrap", oldScrap);
            Set(player, "hp", oldHp);
            Set(player, "DamageVersion", oldVersion);
        }
    }

    static void CheckDifficulty(EnemyManager manager)
    {
        float previous = GameTimeManager.Instance.ElapsedSeconds;
        try
        {
            Set(GameTimeManager.Instance, "ElapsedSeconds", 0f);
            float firstHealth = Get<float>(manager, "EnemyHealthMultiplier");
            float firstDamage = Get<float>(manager, "EnemyDamageMultiplier");
            int firstCount = Get<System.Collections.Generic.List<float>>(manager, "spawnTimes").Count;
            Set(GameTimeManager.Instance, "ElapsedSeconds", 180f);
            Call(manager, "BeginSpawnCycle", false);
            Require(Get<float>(manager, "EnemyHealthMultiplier") > firstHealth &&
                Get<float>(manager, "EnemyDamageMultiplier") > firstDamage &&
                Get<System.Collections.Generic.List<float>>(manager, "spawnTimes").Count > firstCount,
                "Elapsed time increases wave count, enemy health and damage");
            var temporary = new GameObject("Difficulty validation enemy").AddComponent<Enemy>();
            try
            {
                temporary.maxHp = 10;
                Call(temporary, "InitScaled", 2f, 1.5f);
                Require(Get<int>(temporary, "hp") == 20 && Mathf.Approximately(Get<float>(temporary, "HealthFraction"), 1f),
                    "Scaled enemies initialize with a full health bar");
                Call(temporary, "InitScaled", 1f, 1f);
                Require(Get<int>(temporary, "hp") == 10, "Reusing enemies does not compound health growth");
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary.gameObject); }
        }
        finally
        {
            Set(GameTimeManager.Instance, "ElapsedSeconds", previous);
            Call(manager, "BeginSpawnCycle", false);
        }
    }

    static void CheckMissileHoming(EnemyManager manager, PlayerController player)
    {
        Vector2 origin = player.transform.position;
        var target = new GameObject("Homing validation target").AddComponent<Enemy>();
        target.Init();
        target.transform.position = origin + Vector2.right * 8f;
        var pool = Get<object>(manager, "Projectiles");
        var active = Get<System.Collections.IList>(pool, "active");
        int index = active.Count;
        try
        {
            var weapon = new WeaponDefinition { kind = WeaponKind.Missile, range = 30f, homingTurnSpeed = 90f };
            Call(pool, "Fire", origin, Vector2.right, weapon, false);
            object shot = active[index];
            Require(ReferenceEquals(Get<CombatTarget>(shot, "Target"), target), "Missile acquires a live enemy");
            target.transform.position = origin + new Vector2(8f, 6f);
            Call(pool, "SteerMissile", shot, .1f);
            Vector2 direction = Get<Vector2>(shot, "Direction");
            Require(direction.y > 0f && Vector2.Angle(Vector2.right, direction) <= 9.01f,
                "Missile follows a moving target with a bounded turn rate");
            target.gameObject.SetActive(false);
            Call(pool, "SteerMissile", shot, .1f);
            Require(Get<CombatTarget>(shot, "Target") == null, "Missile releases a disabled target safely");
        }
        finally
        {
            if (active.Count > index) Call(pool, "Recycle", index, active[index]);
            UnityEngine.Object.DestroyImmediate(target.gameObject);
        }
    }

    static void CheckUiTeardownAndPausedCamera(PlayerController player)
    {
        var screenType = typeof(PlayerController).Assembly.GetType("AssemblyScreen");
        var detachedScreen = Activator.CreateInstance(screenType, Fields, null,
            new object[] { new VisualElement(), player, (Action)(() => { }) }, null);
        Call(detachedScreen, "Reset");
        Call(detachedScreen, "Close", false);
        Require(true, "Detached assembly can reset and close without reading destroyed style data");

        var camera = UnityEngine.Object.FindAnyObjectByType<CameraFollow>();
        Require(camera, "Follow camera exists");
        camera.Shake();
        Require(Get<float>(camera, "shakeRemaining") > 0f, "Hit shake starts during gameplay");
        Time.timeScale = 0f;
        Call(camera, "LateUpdate");
        Vector3 pausedPosition = camera.transform.position;
        Call(camera, "LateUpdate");
        Require(Get<float>(camera, "shakeRemaining") == 0f && camera.transform.position == pausedPosition,
            "Pausing cancels pending shake and leaves the camera stationary");
        camera.Shake();
        Require(Get<float>(camera, "shakeRemaining") == 0f, "Shake cannot restart while paused");
        Time.timeScale = 1f;
    }

    static void CheckSpawning(EnemyManager manager)
    {
        var enemies = Get<System.Collections.Generic.List<Enemy>>(manager, "enemies");
        int initial = enemies.Count;
        float interval = Get<float>(manager, "spawnInterval");
        int amount = Get<int>(manager, "enemiesPerSpawn");
        int limit = Get<int>(manager, "maxEnemies");
        Set(manager, "spawnInterval", 5f);
        Set(manager, "enemiesPerSpawn", 3);
        Set(manager, "maxEnemies", initial + 7);
        Set(manager, "spawnTimer", 0f);
        Call(manager, "BeginSpawnCycle", true);
        Call(manager, "TickSpawning", 0f);
        Require(enemies.Count == initial + 1, "First enemy spawns immediately without the entire group");
        var times = Get<System.Collections.Generic.List<float>>(manager, "spawnTimes");
        Require(times.Count == 3 && times[0] == 0f && times[1] > 0f && times[2] > times[1] && times[2] < 5f,
            "Remaining spawns are distributed inside the interval");
        Call(manager, "TickSpawning", 5f);
        Require(enemies.Count == initial + 3, "One interval produces the configured total");
        Call(manager, "TickSpawning", 5f);
        Require(enemies.Count == initial + 6, "Next interval distributes another configured group");
        Call(manager, "TickSpawning", 5f);
        Require(enemies.Count == initial + 7, "Partial wave respects maximum enemy count");
        Call(manager, "TickSpawning", 50f);
        Require(enemies.Count == initial + 7, "Full capacity does not exceed the limit");
        Set(manager, "maxEnemies", initial + 20);
        Call(manager, "TickSpawning", 0f);
        Require(enemies.Count == initial + 7, "Full capacity does not bank overdue waves");
        Set(manager, "spawnInterval", interval);
        Set(manager, "enemiesPerSpawn", amount);
        Set(manager, "maxEnemies", limit);
        Set(manager, "spawnTimer", 0f);
        Call(manager, "BeginSpawnCycle", false);
    }

    static void CheckEruption(CrusherBossController boss, PlayerController player, bool takeHit)
    {
        Set(boss, "repetitions", 0);
        Set(boss, "eruptionHitPlayer", false);
        Call(boss, "BuildEruptionAreas");
        var first = boss.EruptionAreas;
        var second = Get<System.Collections.Generic.List<Rect>>(boss, "eruptionB");
        var arena = BossEncounterController.Instance.Arena;
        for (int x = 0; x <= 10; x++)
            for (int y = 0; y <= 10; y++)
            {
                Vector2 p = new(Mathf.Lerp(arena.xMin, arena.xMax, (x + .1f) / 11f),
                    Mathf.Lerp(arena.yMin, arena.yMax, (y + .1f) / 11f));
                bool covered = false;
                foreach (var rect in first) covered |= rect.Contains(p);
                foreach (var rect in second) covered |= rect.Contains(p);
                Require(covered, "A/B layouts leave no permanent safe location");
            }
        Vector2 firstSafe = second[0].center;
        Vector2 secondSafe = first[0].center;
        MovePlayer(player, takeHit ? first[0].center : firstSafe);
        var visual = boss.GetComponent("CrusherBossVisual");
        Call(boss, "EnterState", CrusherBossController.BossState.EruptReady);
        Call(visual, "LateUpdate");
        Set(boss, "stateElapsed", Get<float>(boss, "eruptReadyDuration") * .9f);
        Call(visual, "LateUpdate");
        uint version = Get<uint>(player, "DamageVersion");
        Call(boss, "EnterState", CrusherBossController.BossState.Erupt);
        Call(visual, "LateUpdate");
        Require((Get<uint>(player, "DamageVersion") != version) == takeHit, "Eruption damage matches telegraphed area");
        Set(boss, "stateElapsed", 10f);
        Call(boss, "FixedUpdate");
        Require(boss.State == CrusherBossController.BossState.EruptWait, "First eruption waits even after a hit");
        Set(boss, "stateElapsed", 10f);
        Call(boss, "FixedUpdate");
        Require(boss.State == CrusherBossController.BossState.EruptReady, "Second eruption receives its own warning");
        Call(visual, "LateUpdate");
        Set(boss, "stateElapsed", Get<float>(boss, "eruptReadyDuration") * .9f);
        Call(visual, "LateUpdate");
        MovePlayer(player, secondSafe);
        if (!takeHit) player.TakeDamage(1); // Unrelated damage must not invalidate the eruption dodge reward.
        Call(boss, "EnterState", CrusherBossController.BossState.Erupt);
        Set(boss, "stateElapsed", 10f);
        Call(boss, "FixedUpdate");
        Require(boss.State == (takeHit ? CrusherBossController.BossState.Delay : CrusherBossController.BossState.Groggy),
            "Only eruption hits determine groggy eligibility");
        Set(player, "hp", player.MaxHp);
    }

    static void MovePlayer(PlayerController player, Vector2 position)
    {
        player.GetComponent<Rigidbody2D>().position = position;
        Physics2D.SyncTransforms();
    }

    static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Fields).Invoke(target, args);
    static T Get<T>(object target, string name)
    {
        var type = target.GetType();
        var field = type.GetField(name, Fields);
        return field != null ? (T)field.GetValue(target) : (T)type.GetProperty(name, Fields).GetValue(target);
    }
    static void Set(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(name, Fields);
        if (field != null) field.SetValue(target, value);
        else type.GetField("<" + name + ">k__BackingField", Fields).SetValue(target, value);
    }
    static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
