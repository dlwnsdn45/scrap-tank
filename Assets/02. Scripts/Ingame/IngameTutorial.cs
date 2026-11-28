using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Lessons observe committed gameplay results, rather than advancing on key presses.
internal sealed class IngameTutorial : IDisposable
{
    internal static TutorialStep CurrentStep { get; private set; } = TutorialStep.Welcome;
    static bool fireArmed;
    internal static float MovementSpeedScale => SaveTutorialSession.Active && CurrentStep is TutorialStep.Boost or TutorialStep.BoostAgain ? .02f : 1f;
    internal static bool Allows(TutorialAction action) => !SaveTutorialSession.Active ||
        (TutorialRules.Allows(CurrentStep, action) && (action != TutorialAction.Fire || fireArmed));

    readonly PlayerController player;
    readonly VisualElement root, layer, canvas, guide, strip, focus, modal, toast, marker;
    readonly VisualElement guideFill, stripFill, dim;
    readonly VisualElement[] dimPanels;
    readonly Label heading, instruction, progress, stripTitle, stripBody, modalTitle, modalBody;
    readonly Button next, modalNext, modalNo;
    readonly VisualElement movementKeys;
    readonly Label[] movementKeyLabels;
    readonly Camera view;
    readonly List<GameObject> practiceObjects = new();
    readonly List<LootPickup> scrapPractice = new();
    readonly List<TutorialTarget> fireTargets = new();
    LootPickup pickup;
    TutorialTarget target;
    RepairZone station;
    VisualElement focusTarget;
    Transform markerTarget;
    Vector2 previousPosition, startMouse;
    float previousAngle, aimSeconds, enteredAt, satisfiedAt, toastUntil, uiScale = 1;
    readonly float[] movementSeconds = new float[4];
    const float DirectionPracticeSeconds = .4f;
    const float AimPracticeSeconds = 1.2f;
    int movementMask;
    uint baseline;
    bool started, disposed, finishing, stepSatisfied;

    internal IngameTutorial(VisualElement root, PlayerController player)
    {
        this.root = root;
        this.player = player;
        view = Camera.main;
        layer = root.Q("tutorial-layer");
        canvas = layer.Q("tutorial-canvas");
        guide = layer.Q("tutorial-guide"); strip = layer.Q("tutorial-strip");
        focus = layer.Q("tutorial-focus"); modal = layer.Q("tutorial-modal");
        toast = layer.Q("tutorial-toast"); marker = layer.Q("tutorial-marker");
        heading = layer.Q<Label>("tutorial-heading"); instruction = layer.Q<Label>("tutorial-instruction");
        progress = layer.Q<Label>("tutorial-progress");
        stripTitle = layer.Q<Label>("tutorial-strip-title"); stripBody = layer.Q<Label>("tutorial-strip-body");
        modalTitle = layer.Q<Label>("tutorial-modal-title"); modalBody = layer.Q<Label>("tutorial-modal-body");
        guideFill = layer.Q("tutorial-guide-fill"); stripFill = layer.Q("tutorial-strip-fill");
        next = layer.Q<Button>("tutorial-next"); modalNext = layer.Q<Button>("tutorial-modal-next");
        modalNo = layer.Q<Button>("tutorial-modal-no");
        dim = layer.Q("tutorial-dim");
        dimPanels = new[] { layer.Q("tutorial-dim-top"), layer.Q("tutorial-dim-bottom"), layer.Q("tutorial-dim-left"), layer.Q("tutorial-dim-right") };
        movementKeys = layer.Q("tutorial-movement-keys");
        movementKeyLabels = new[] { layer.Q<Label>("tutorial-key-w"), layer.Q<Label>("tutorial-key-a"), layer.Q<Label>("tutorial-key-s"), layer.Q<Label>("tutorial-key-d") };
        next.clicked += Confirm; modalNext.clicked += Confirm;
        modalNo.clicked += SkipWelcome;
        layer.Q<Button>("tutorial-skip").clicked += () => Finish(false);
        CurrentStep = TutorialStep.Welcome;
        layer.style.display = DisplayStyle.None;
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.RegisterCallback<PointerDownEvent>(GuardPointer, TrickleDown.TrickleDown);
        root.RegisterCallback<ClickEvent>(GuardClick, TrickleDown.TrickleDown);
    }

    void OnGeometryChanged(GeometryChangedEvent e) => Resize();

    bool CanClick(VisualElement element)
    {
        if (!SaveTutorialSession.Active || element == null) return true;
        if (next == element || next.Contains(element) || modalNext == element || modalNext.Contains(element)) return true;
        if (CurrentStep == TutorialStep.Welcome && (modalNo == element || modalNo.Contains(element))) return true;
        var allowed = CurrentStep == TutorialStep.SettingsClose ? root.Q("settings-panel") :
            TutorialRules.IsUiPractice(CurrentStep) ? focusTarget : null;
        return allowed != null && (allowed == element || allowed.Contains(element));
    }
    void GuardPointer(PointerDownEvent e) { if (!CanClick(e.target as VisualElement)) e.StopImmediatePropagation(); }
    void GuardClick(ClickEvent e) { if (!CanClick(e.target as VisualElement)) e.StopImmediatePropagation(); }

    internal void Refresh()
    {
        if (disposed || finishing) return;
        bool active = SaveTutorialSession.Active;
        bool ready = active && !ScrapSceneTransition.IsTransitioning && !SceneFadeController.IsTransitioning;
        layer.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
        if (!ready) return;
        if (!started)
        {
            started = true;
            player.PrepareTutorialLoadout();
            BeginStep();
        }
        layer.BringToFront();
        float value = Observe();
        if (value >= 1 && TutorialRules.IsUiPractice(CurrentStep))
        {
            Advance();
            return;
        }
        if (!TutorialRules.NeedsConfirmation(CurrentStep) && value >= 1 && !stepSatisfied)
        {
            stepSatisfied = true;
            satisfiedAt = Time.unscaledTime;
            if (CurrentStep == TutorialStep.Move)
                stripBody.text = "좋아요! 고철을 모두 모았네요.";
        }
        if (stepSatisfied) value = 1;
        guideFill.style.width = stripFill.style.width = Length.Percent(Mathf.Clamp01(value) * 100);
        // Leave time to read each lesson and see its result before automatic advance.
        if (stepSatisfied && Time.unscaledTime - enteredAt >= 3f && Time.unscaledTime - satisfiedAt >= 1.25f)
            Advance();
        next.SetEnabled(TutorialRules.NeedsConfirmation(CurrentStep) && Time.unscaledTime - enteredAt >= .65f);
        modalNext.SetEnabled(Time.unscaledTime - enteredAt >= .65f);
        modalNo.SetEnabled(Time.unscaledTime - enteredAt >= .65f);
        toast.style.display = Time.unscaledTime < toastUntil ? DisplayStyle.Flex : DisplayStyle.None;
        PositionGuidance();
    }

    float Observe()
    {
        switch (CurrentStep)
        {
            case TutorialStep.Move:
                Vector2 position = player.transform.position;
                Vector2 delta = position - previousPosition;
                var keyboard = Keyboard.current;
                float practiceDelta = Mathf.Min(Time.unscaledDeltaTime, .05f);
                if (keyboard != null)
                {
                    if (keyboard.wKey.isPressed && delta.y > .0001f) movementSeconds[0] += practiceDelta;
                    if (keyboard.aKey.isPressed && delta.x < -.0001f) movementSeconds[1] += practiceDelta;
                    if (keyboard.sKey.isPressed && delta.y < -.0001f) movementSeconds[2] += practiceDelta;
                    if (keyboard.dKey.isPressed && delta.x > .0001f) movementSeconds[3] += practiceDelta;
                }
                previousPosition = position;
                int completed = 0;
                float movementProgress = 0;
                for (int i = 0; i < movementKeyLabels.Length; i++)
                {
                    if (movementSeconds[i] >= DirectionPracticeSeconds) movementMask |= 1 << i;
                    movementProgress += Mathf.Clamp01(movementSeconds[i] / DirectionPracticeSeconds);
                    bool done = (movementMask & (1 << i)) != 0;
                    if (done) completed++;
                    movementKeyLabels[i].style.backgroundColor = done ? IngameUIStyle.Teal : new Color(.16f, .20f, .21f);
                    movementKeyLabels[i].text = (done ? "✓ " : "") + "WASD"[i];
                }
                int collected = 0;
                foreach (var item in scrapPractice) if (!item || !item.gameObject.activeSelf) collected++;
                stripBody.text = $"WASD로 이동할 수 있습니다.\n고철을 주워 보세요. ({collected}/3)";
                foreach (var item in scrapPractice)
                    if (item && item.gameObject.activeSelf) { markerTarget = item.transform; break; }
                return Mathf.Min(completed == 4 ? 1 : Mathf.Min(.99f, movementProgress / 4f), collected / 3f);
            case TutorialStep.Aim:
                Vector2 mouse = Mouse.current?.position.ReadValue() ?? startMouse;
                float angle = player.transform.eulerAngles.z;
                if (Vector2.Distance(startMouse, mouse) > .3f && Mathf.Abs(Mathf.DeltaAngle(previousAngle, angle)) > .03f)
                    aimSeconds += Mathf.Min(Time.unscaledDeltaTime, .05f);
                startMouse = mouse; previousAngle = angle;
                return aimSeconds / AimPracticeSeconds;
            case TutorialStep.Boost:
            case TutorialStep.BoostAgain: return player.BoostVersion != baseline && target && target.Hit ? 1 : 0;
            case TutorialStep.Recharge: return player.BoostReadyFraction;
            case TutorialStep.Pickup:
            case TutorialStep.Swap:
            case TutorialStep.AssemblyInstall: return player.InstallVersion != baseline ? 1 : 0;
            case TutorialStep.Fire:
                if (!fireArmed)
                {
                    Vector2 fireMouse = Mouse.current?.position.ReadValue() ?? startMouse;
                    fireArmed = Vector2.Distance(startMouse, fireMouse) > .1f;
                }
                int destroyed = 0;
                foreach (var item in fireTargets)
                    if (item && item.Destroyed) destroyed++;
                    else if (item) markerTarget = item.transform;
                instruction.text = $"마우스로 방향을 돌려\n빨간 표적을 부수세요. ({destroyed}/3)";
                return destroyed / 3f;
            case TutorialStep.AssemblyOpen: return player.AssemblyOpen ? 1 : 0;
            case TutorialStep.AssemblyMove: return player.AssemblyMoveVersion != baseline ? 1 : 0;
            case TutorialStep.AssemblyDismantle: return player.DismantleVersion != baseline ? 1 : 0;
            case TutorialStep.AssemblyClose: return !player.AssemblyOpen ? 1 : 0;
            case TutorialStep.FieldRepair: return player.FieldRepairVersion != baseline ? 1 : player.FieldRepairProgress * .95f;
            case TutorialStep.StationRepair: return player.StationRepairVersion != baseline ? 1 : station ? station.Progress * .95f : 0;
            case TutorialStep.PauseOpen: return player.PauseOpen ? 1 : 0;
            case TutorialStep.SettingsOpen: return SettingsVisible ? 1 : 0;
            case TutorialStep.SettingsClose: return !SettingsVisible ? 1 : 0;
            case TutorialStep.PauseClose: return !player.PauseOpen ? 1 : 0;
            default: return 0;
        }
    }

    bool SettingsVisible => root.Q("settings-panel").style.display.value != DisplayStyle.None;

    void BeginStep()
    {
        stepSatisfied = false; enteredAt = Time.unscaledTime; satisfiedAt = -1; focusTarget = null; markerTarget = null;
        previousPosition = player.transform.position; movementMask = 0;
        Array.Clear(movementSeconds, 0, movementSeconds.Length); aimSeconds = 0;
        previousAngle = player.transform.eulerAngles.z;
        startMouse = Mouse.current?.position.ReadValue() ?? Vector2.zero;
        fireArmed = false;
        Show(layer.Q("tutorial-skip"), false);
        Show(modalNo, CurrentStep == TutorialStep.Welcome);
        Show(guide, true); Show(strip, false); Show(modal, false);
        Show(movementKeys, CurrentStep == TutorialStep.Move);
        strip.style.height = CurrentStep == TutorialStep.Move ? 136 : 90;
        guide.Add(next);
        Show(next, TutorialRules.NeedsConfirmation(CurrentStep));
        next.SetEnabled(false); modalNext.SetEnabled(false); modalNo.SetEnabled(false);
        next.text = TutorialRules.NeedsConfirmation(CurrentStep) ? "다음" : "조작을 완료하면 다음으로";
        string title = "", body = "", key = "";
        switch (CurrentStep)
        {
            case TutorialStep.Welcome:
                Show(guide, false); Show(modal, true);
                modalTitle.text = "고물전차에 오신 것을 환영합니다";
                modalBody.text = "UI와 조작을 같이 배워 볼까요?";
                modalNext.text = "좋아요";
                break;
            case TutorialStep.Move:
                CleanupLoot(); scrapPractice.Clear();
                for (int i = 0; i < 3; i++)
                    scrapPractice.Add(Drop(FindSpot(6 + i, Quaternion.Euler(0, 0, i * 120) * Vector2.right), 10));
                title = "이동과 고철 수집"; body = "WASD로 이동할 수 있습니다.\n고철을 주워 보세요."; key = "W A S D";
                Show(guide, false); Show(strip, true); markerTarget = player.transform;
                break;
            case TutorialStep.Aim:
                title = "방향 돌리기"; body = "차체는 마우스를 바라봅니다.\n마우스를 1.2초 이상 움직여 보세요."; key = "마우스 이동";
                markerTarget = player.transform;
                break;
            case TutorialStep.Boost:
            case TutorialStep.BoostAgain:
                var ramObject = PracticeObject("Tutorial Ram Target", FindSpot(8, CurrentStep == TutorialStep.Boost ? Vector2.right : Vector2.up), new Vector3(1.3f, 1.3f, .25f), IngameUIStyle.Red);
                ramObject.AddComponent<BoxCollider2D>().isTrigger = true;
                target = ramObject.AddComponent<TutorialTarget>(); markerTarget = target.transform;
                title = CurrentStep == TutorialStep.Boost ? "부스터" : "한 번 더 돌진!";
                body = "빨간 표적을 향해\nSpace로 돌진해 보세요."; key = "표적 쪽으로 Space";
                baseline = player.BoostVersion; focusTarget = root.Q("boost-panel");
                break;
            case TutorialStep.Recharge:
                if (target) target.gameObject.SetActive(false);
                title = "부스터 충전"; body = "노란 막대가 차면 다음 표적에\n다시 돌진할 수 있습니다."; key = "부스터 충전 기다리기";
                focusTarget = root.Q("boost-panel");
                break;
            case TutorialStep.Pickup:
                CleanupLoot(); player.PrepareTutorialLoadout(); baseline = player.InstallVersion;
                pickup = Drop(FindSpot(8), 0, WeaponKind.MachineGun);
                title = "무기 장착"; body = "기관총을 주우면 빈 칸에\n자동으로 장착됩니다."; key = "기관총 쪽으로 이동";
                markerTarget = pickup.transform; focusTarget = root.Q("loadout-panel");
                break;
            case TutorialStep.Fire:
                if (target) target.gameObject.SetActive(false);
                fireTargets.Clear();
                Vector2 initialAim = player.transform.up;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 direction = Quaternion.Euler(0, 0, 90 + i * 90) * initialAim;
                    var obj = PracticeObject("Tutorial Fire Target " + (i + 1), FindSpot(7, direction, initialAim), new Vector3(1.3f, 1.3f, .25f), IngameUIStyle.Red);
                    obj.AddComponent<BoxCollider2D>().isTrigger = true;
                    fireTargets.Add(obj.AddComponent<TutorialTarget>());
                }
                target = fireTargets[0];
                title = "공격"; body = "마우스로 방향을 돌려\n빨간 표적 3개를 부수세요."; key = "표적 쪽으로 마우스 이동";
                markerTarget = target.transform;
                break;
            case TutorialStep.Swap:
                CleanupLoot(); if (target) target.gameObject.SetActive(false);
                player.PrepareTutorialLoadout(true); baseline = player.InstallVersion;
                pickup = Drop((Vector2)player.transform.position + Vector2.right * .5f, 0, WeaponKind.Shotgun);
                title = "무기 교체"; body = "무기가 많으면 교체해야 합니다.\nE를 누른 채 면을 고르고\nE를 떼면 무기가 바뀝니다."; key = "E를 누르고 면 고르기";
                markerTarget = pickup.transform;
                break;
            case TutorialStep.AssemblyOpen:
                CleanupLoot(); player.PrepareTutorialLoadout();
                player.Layout.TryInstall(0, WeaponKind.MachineGun, 1, out _, out _);
                pickup = Drop((Vector2)player.transform.position + Vector2.right * .5f, 0, WeaponKind.Shotgun);
                title = "조립"; body = "Tab을 누르면 조립 화면이 열립니다.\n한번 열어 볼까요?"; key = "Tab";
                focusTarget = root.Q("game-key-hints");
                break;
            case TutorialStep.AssemblyMove:
                baseline = player.AssemblyMoveVersion;
                title = "부품 옮기기"; body = "부품은 원하는 칸으로 옮길 수 있죠.\n기관총을 빈 칸으로 끌어 보세요."; key = "기관총 드래그";
                focusTarget = root.Q("assembly-board");
                break;
            case TutorialStep.AssemblyDismantle:
                baseline = player.DismantleVersion;
                title = "부품 분해"; body = "[분해]를 누르면 고철로 바뀝니다.\n기관총을 분해해 볼까요?"; key = "분해 버튼";
                focusTarget = root.Q("assembly-dismantle");
                break;
            case TutorialStep.AssemblyInstall:
                baseline = player.InstallVersion;
                title = "부품 장착"; body = "이번에는 산탄총을 달아 볼까요?\n[장착]을 눌러 보세요."; key = "장착 버튼";
                focusTarget = root.Q("assembly-install");
                break;
            case TutorialStep.AssemblyClose:
                title = "조립 마치기"; body = "잘하셨네요!\n이제 조립 화면을 닫아 볼까요?"; key = "Tab 또는 ×";
                focusTarget = root.Q("assembly-close");
                break;
            case TutorialStep.FieldRepair:
                CleanupLoot(); player.PrepareTutorialRepair(false); baseline = player.FieldRepairVersion;
                title = "부품 수리"; body = "R을 길게 누르면 수리할 수 있죠.\n기관총을 고쳐 볼까요?"; key = "R 길게 누르기";
                focusTarget = root.Q("loadout-panel"); markerTarget = player.transform;
                break;
            case TutorialStep.StationRepair:
                player.PrepareTutorialRepair(true); baseline = player.StationRepairVersion;
                var stationObject = PracticeObject("Tutorial Repair Station", FindSpot(3.5f), new Vector3(2.2f, 2.2f, .1f), IngameUIStyle.Teal);
                stationObject.AddComponent<CircleCollider2D>().isTrigger = true;
                station = stationObject.AddComponent<RepairZone>(); station.TutorialZone = true;
                title = "수리소"; body = "수리소에서는 차체도 고칠 수 있죠.\n안으로 들어가 E를 1초간 눌러 보세요."; key = "수리소에서 E 누르기";
                markerTarget = station.transform; focusTarget = root.Q("health-panel");
                break;
            case TutorialStep.PauseOpen:
                title = "일시정지"; body = "잠깐 쉬고 싶을 때도 있죠.\nⅡ 또는 Esc로 멈춰 보세요."; key = "Esc 또는 Ⅱ";
                focusTarget = root.Q("hud-pause");
                break;
            case TutorialStep.SettingsOpen:
                title = "설정"; body = "소리와 화면도 바꿀 수 있습니다.\n[설정]을 눌러 볼까요?"; key = "설정 버튼";
                focusTarget = root.Q("pause-settings");
                break;
            case TutorialStep.SettingsClose:
                title = "설정 확인"; body = "편한 대로 조절해 보세요.\n다 봤다면 [돌아가기]를 누르세요."; key = "돌아가기 버튼";
                focusTarget = root.Q("settings-back");
                break;
            case TutorialStep.PauseClose:
                title = "준비됐나요?"; body = "[계속하기]를 누르면\n차량을 초기화하고 전투를 시작합니다."; key = "계속하기 버튼";
                focusTarget = root.Q("pause-resume");
                break;
            case TutorialStep.Health:
                title = "체력"; body = "차체 체력입니다.\n0이 되면 죽죠;;"; key = "차체 체력";
                focusTarget = root.Q("health-panel");
                break;
            case TutorialStep.Timer:
                title = "시간"; body = "플레이 시간입니다.\n시간이 지나면 무기 칸이 열리고\n적도 강해집니다.\n무언가 소환될 수도?"; key = "플레이 시간";
                focusTarget = root.Q("clock-panel");
                break;
            case TutorialStep.Minimap:
                title = "미니맵"; body = "흰색은 내 차\n청록색은 수리소\n노란색은 적입니다."; key = "미니맵";
                focusTarget = root.Q("minimap");
                break;
        }
        heading.text = title; instruction.text = body;
        progress.text = $"조작 연습  {(int)CurrentStep + 1} / {(int)TutorialStep.PauseClose + 1}";
        stripTitle.text = title; stripBody.text = body;
        ((Label)marker).text = "▼ " + key;
        guideFill.style.width = stripFill.style.width = 0;
        Resize();
    }

    void Confirm()
    {
        if (!started || finishing || !SaveTutorialSession.Active || Time.unscaledTime - enteredAt < .65f) return;
        if (!TutorialRules.NeedsConfirmation(CurrentStep)) return;
        Advance();
    }

    void SkipWelcome()
    {
        if (!started || CurrentStep != TutorialStep.Welcome || Time.unscaledTime - enteredAt < .65f) return;
        Finish(false);
    }

    void Advance()
    {
        if (CurrentStep == TutorialStep.PauseClose) { Finish(true); return; }
        if (CurrentStep is TutorialStep.Boost or TutorialStep.BoostAgain)
        {
            if (target) RemovePracticeObject(target.gameObject);
            target = null;
        }
        if (CurrentStep == TutorialStep.Fire)
        {
            foreach (var item in fireTargets)
                if (item) RemovePracticeObject(item.gameObject);
            fireTargets.Clear(); target = null;
        }
        if (CurrentStep == TutorialStep.StationRepair)
        {
            if (station) RemovePracticeObject(station.gameObject);
            station = null;
        }
        ((Label)toast).text = "✓ " + (heading.text.Length > 0 ? heading.text : "연습 시작");
        toastUntil = Time.unscaledTime + .8f;
        CurrentStep++;
        BeginStep();
    }

    void RemovePracticeObject(GameObject obj)
    {
        obj.SetActive(false);
        practiceObjects.Remove(obj);
        UnityEngine.Object.Destroy(obj);
    }

    void Finish(bool completed)
    {
        if (finishing || !SaveTutorialSession.Active) return;
        string path = player.gameObject.scene.path;
        if (!Application.CanStreamedLevelBeLoaded(path))
        {
            Debug.LogError("Tutorial reset requires the Ingame scene in the build scene list.");
            return;
        }
        finishing = true;
        // Reload restores every scene-owned object, runtime counter, loadout and timer,
        // including ejected parts and projectiles created by real practice actions.
        player.SetAssemblyOpen(false); player.SetPauseOpen(false);
        SaveTutorialSession.End(completed && CurrentStep == TutorialStep.PauseClose);
        IngameUIStyle.LoadScene(path);
    }

    LootPickup Drop(Vector2 position, int scrap, WeaponKind? kind = null)
    {
        var item = LootPickup.Drop(position, player, scrap, kind);
        practiceObjects.Add(item.gameObject); return item;
    }

    Vector2 FindSpot(float distance) => FindSpot(distance, Vector2.right);

    Vector2 FindSpot(float distance, Vector2 preferred, Vector2 avoidDirection = default)
    {
        Vector2 origin = player.transform.position;
        var follow = view ? view.GetComponent<CameraFollow>() : null;
        Vector2 best = origin;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2 direction = Quaternion.Euler(0, 0, attempt * 22.5f) * preferred;
            Vector2 candidate = origin + direction * distance;
            if (follow) candidate = follow.ClampToMap(candidate, Vector2.one * 1.5f);
            if (Vector2.Distance(origin, candidate) < distance * .7f) continue;
            if (avoidDirection != Vector2.zero && Vector2.Angle(avoidDirection, candidate - origin) < 75) continue;
            bool crowded = false;
            foreach (var item in practiceObjects)
                if (item && item.activeSelf && Vector2.Distance(item.transform.position, candidate) < 3) { crowded = true; break; }
            if (crowded) continue;
            bool blocked = false;
            foreach (var collider in Physics2D.OverlapCircleAll(candidate, .6f))
                if (!collider.isTrigger && collider != player.BodyCollider) { blocked = true; break; }
            if (!blocked) return candidate;
            best = candidate;
        }
        return best;
    }

    GameObject PracticeObject(string name, Vector2 position, Vector3 size, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.position = new Vector3(position.x, position.y, -.2f);
        obj.transform.localScale = size;
        // Keep the primitive's 3D collider off the root that receives 2D physics.
        // Destroy is deferred, so removing it from the same object is insufficient.
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual"; visual.transform.SetParent(obj.transform, false);
        var collider = visual.GetComponent<Collider>(); collider.enabled = false; UnityEngine.Object.Destroy(collider);
        var renderer = visual.GetComponent<Renderer>(); renderer.sortingOrder = 2;
        var properties = new MaterialPropertyBlock(); properties.SetColor("_Color", color); renderer.SetPropertyBlock(properties);
        practiceObjects.Add(obj); return obj;
    }

    void CleanupLoot()
    {
        foreach (var item in UnityEngine.Object.FindObjectsByType<LootPickup>())
            if (item.TutorialPickup) { item.gameObject.SetActive(false); UnityEngine.Object.Destroy(item.gameObject); }
    }

    void Resize()
    {
        float w = root.layout.width, h = root.layout.height;
        if (root.panel == null || w <= 0 || h <= 0 || !float.IsFinite(w) || !float.IsFinite(h)) return;
        uiScale = Mathf.Min(w / 1200f, h / 700f);
        canvas.style.width = w / uiScale; canvas.style.height = h / uiScale;
        canvas.style.scale = new Scale(Vector3.one * uiScale);
    }

    void PositionGuidance()
    {
        float w = root.layout.width / uiScale, h = root.layout.height / uiScale;
        float guideHeight = float.IsFinite(guide.layout.height) && guide.layout.height > 0 ? guide.layout.height : 220;
        Rect? highlight = null;
        if (focusTarget != null && focusTarget.resolvedStyle.display != DisplayStyle.None)
        {
            Vector2 min = root.WorldToLocal(focusTarget.worldBound.min) / uiScale;
            Vector2 max = root.WorldToLocal(focusTarget.worldBound.max) / uiScale;
            highlight = Rect.MinMaxRect(min.x - 5, min.y - 5, max.x + 5, max.y + 5);
        }
        Show(focus, highlight.HasValue);
        Show(dim, highlight.HasValue && CurrentStep is not (TutorialStep.Pickup or TutorialStep.StationRepair));
        if (highlight.HasValue)
        {
            Rect r = highlight.Value;
            float left = Mathf.Clamp(r.xMin, 0, w), right = Mathf.Clamp(r.xMax, left, w);
            float top = Mathf.Clamp(r.yMin, 0, h), bottom = Mathf.Clamp(r.yMax, top, h);
            SetDimPanel(dimPanels[0], 0, 0, w, top);
            SetDimPanel(dimPanels[1], 0, bottom, w, h - bottom);
            SetDimPanel(dimPanels[2], 0, top, left, bottom - top);
            SetDimPanel(dimPanels[3], right, top, w - right, bottom - top);
            focus.style.left = r.x; focus.style.top = r.y; focus.style.width = r.width; focus.style.height = r.height;
            focus.style.opacity = .65f + .35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3));
            float x = r.xMax + 14;
            if (x + 340 > w - 12) x = r.xMin - 354;
            if (x < 12) x = Mathf.Clamp(r.center.x - 170, 12, Mathf.Max(12, w - 352));
            guide.style.left = x;
            float y = Mathf.Clamp(r.center.y - guideHeight * .5f, 92, Mathf.Max(92, h - guideHeight - 12));
            // The front slots stay exposed while the coach uses the board's spare lower-left area.
            if (r.width > w * .45f) { guide.style.left = r.xMin + 12; y = Mathf.Max(92, h - guideHeight - 30); }
            guide.style.top = y;
        }
        else
        {
            guide.style.left = 16; guide.style.top = 100;
        }
        Show(marker, markerTarget && !player.AssemblyOpen && !player.PauseOpen);
        if (markerTarget && view)
        {
            Vector3 pos = view.WorldToViewportPoint(markerTarget.position);
            marker.style.left = Mathf.Clamp(pos.x * w - 120, 8, Mathf.Max(8, w - 248));
            marker.style.top = Mathf.Clamp((1 - pos.y) * h - 92, 90, Mathf.Max(90, h - 88));
        }
    }

    static void Show(VisualElement element, bool show) => element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

    static void SetDimPanel(VisualElement panel, float x, float y, float width, float height)
    {
        panel.style.left = x; panel.style.top = y;
        panel.style.width = width; panel.style.height = height;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.UnregisterCallback<PointerDownEvent>(GuardPointer, TrickleDown.TrickleDown);
        root.UnregisterCallback<ClickEvent>(GuardClick, TrickleDown.TrickleDown);
        foreach (var obj in practiceObjects)
            if (obj) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
        practiceObjects.Clear();
        if (layer.panel != null) layer.style.display = DisplayStyle.None;
    }
}
