using System;
using UnityEngine;
using UnityEngine.UI;
using T = ExpeditionTheme;

/// <summary>Player status and skills share the inventory's visual language.</summary>
[DisallowMultipleComponent]
public sealed class GameHUDOverlay : MonoBehaviour
{
    [SerializeField] Player_State playerState;
    [SerializeField] Player player;
    [SerializeField, Min(1)] int maxStamina = 100;
    [SerializeField, Min(0)] int currentStamina = 100;
    [SerializeField] bool showStaminaBar = true;
    [SerializeField] bool hideLegacyPlayerHud = true;
    NetworkBootstrap bootstrap;
    RectTransform root, playerPanel, timerPanel;
    Text playerNameText, hpText, staminaText, timerText, levelText;
    Text stormLabel,stormValue;
    Image stormProgress;
    readonly GameObject[] debuffBadges=new GameObject[4];
    Image hpFill, hpTrail, staminaFill, teamMark, healthAccent;
    RawImage portraitRaw;
    SkillHudCard eCard, qCard;
    float nextLateBindAt, hpRatio = 1, trailingHp = 1, damageHold;
    bool timerRunning, clockFinished;
    float runStartTime, frozenSeconds;

    public void Build()
    {
        if (root != null) return;
        root = T.Rect("Expedition HUD", transform, 0, 0, 0, 0);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        playerPanel = T.Rect("Player Command Panel", root, 32, 32, 404, 230);
        playerPanel.anchorMin = playerPanel.anchorMax = playerPanel.pivot = Vector2.zero;
        // Content uses the panel centre; the panel itself stays pinned to the safe lower-left corner.
        var content = T.Rect("Profile and Skills", playerPanel, 0, 0, 404, 230);
        var backing = T.Box("Profile Surface", content, 0, 45, 404, 140, T.Ink);
        backing.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .28f);
        T.Box("Profile Rule", content, 0, 114, 368, 1, new Color(T.Gold.r, T.Gold.g, T.Gold.b, .35f));
        healthAccent = T.Box("Health Accent", content, -201, 45, 2, 104, T.Gold);
        var rim = T.Box("Portrait Rim", content, -132, 45, 110, 110, new Color(.30f, .29f, .24f));
        var mask = T.Box("Portrait Mask", rim.transform, 0, 0, 106, 106, Color.white);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        T.Box("Portrait Backdrop", mask.transform, 0, 0, 106, 106, T.Card);
        portraitRaw = T.Rect("Player Portrait", mask.transform, 0, 0, 106, 106).gameObject.AddComponent<RawImage>();
        portraitRaw.raycastTarget = false; portraitRaw.color = Color.clear;
        var badge = T.Box("Level Badge", rim.transform, 0, -48, 62, 22, T.Ink);
        levelText = T.Label("Level", badge.transform, 0, 0, 60, 22, 12, T.Gold, "LV. 01", TextAnchor.MiddleCenter);
        teamMark = T.Box("Team Accent", content, -57, 89, 3, 16, T.Gold);
        playerNameText = T.Label("Player Name", content, 65, 89, 226, 29, 21, T.White, "PLAYER");
        playerNameText.resizeTextForBestFit = true; playerNameText.resizeTextMinSize = 14; playerNameText.resizeTextMaxSize = 21;
        T.Label("Health Label", content, -25, 56, 62, 20, 12, T.Muted, "체력");
        hpText = T.Label("Health Value", content, 102, 56, 152, 22, 16, T.White, "100 / 100", TextAnchor.MiddleRight);
        var healthTrack = T.Box("Health Track", content, 62, 36, 238, 12, new Color(.16f, .09f, .105f));
        hpTrail = Fill("Damage Trail", healthTrack.transform, new Color(.88f, .60f, .38f));
        hpFill = Fill("Health Fill", healthTrack.transform, new Color(.76f, .25f, .29f));
        var staminaTrack = T.Box("Stamina Track", content, 62, 14, 238, 5, new Color(.12f, .17f, .17f));
        staminaFill = Fill("Stamina Fill", staminaTrack.transform, new Color(.48f, .70f, .63f), 0);
        staminaText = T.Label("Stamina Value", content, 62, -3, 238, 19, 12, T.Muted, "스태미나  100 / 100", TextAnchor.MiddleRight);
        staminaTrack.gameObject.SetActive(showStaminaBar); staminaText.gameObject.SetActive(showStaminaBar);
        string[] debuffs={"화상","빙결","기절","중독"};
        Color[] colors={new Color(1,.42f,.2f),new Color(.40f,.78f,1),new Color(1,.84f,.28f),new Color(.60f,.88f,.25f)};
        for(int i=0;i<4;i++){
            var statusBadge=T.Box("Debuff "+debuffs[i],content,-25+i*61,-27,56,23,new Color(.13f,.12f,.15f));
            T.Box("Status Color",statusBadge.transform,-27,0,2,21,colors[i]);
            T.Label("Status",statusBadge.transform,0,0,54,22,12,colors[i],debuffs[i],TextAnchor.MiddleCenter);
            debuffBadges[i]=statusBadge.gameObject;statusBadge.gameObject.SetActive(false);
        }
        eCard = SkillHudCard.Create(content, "E", "검기", 1, new Vector2(-104, -77));
        qCard = SkillHudCard.Create(content, "Q", "전방 검격", 2, new Vector2(104, -77));
        timerPanel = T.Box("Run Timer Panel", root, 0, -20, 540, 88, T.Ink).rectTransform;
        timerPanel.anchorMin = timerPanel.anchorMax = timerPanel.pivot = new Vector2(.5f, 1);
        var clockContent = T.Rect("Clock Content", timerPanel, -174, 0, 192, 58);
        T.Box("Clock Accent", clockContent, -78, 0, 2, 26, T.Gold);
        T.Label("Timer Label", clockContent, 9, 14, 140, 17, 10, T.Muted, "진행 시간", TextAnchor.MiddleCenter);
        timerText = T.Label("Timer Text", clockContent, 9, -6, 140, 31, 24, T.White, "00:00", TextAnchor.MiddleCenter);
        T.Box("Storm Divider",timerPanel,-72,0,1,52,T.Card);
        stormLabel=T.Label("Storm Status",timerPanel,87,21,300,24,13,T.Gold,"자기장 정보 대기 중",TextAnchor.MiddleCenter);
        stormValue=T.Label("Storm Countdown",timerPanel,87,-5,300,26,20,T.White,"—",TextAnchor.MiddleCenter);
        T.Box("Storm Track",timerPanel,87,-28,280,3,T.Card);
        stormProgress=T.Box("Storm Progress",timerPanel,87,-28,280,3,T.Gold);
        stormProgress.rectTransform.pivot=new Vector2(0,.5f);stormProgress.rectTransform.anchoredPosition=new Vector2(-53,-28);
        RefreshHp(100, 100); RefreshStamina();
    }
    static Image Fill(string name, Transform parent, Color color, float inset = 1)
    {
        var image = T.Box(name, parent, 0, 0, 0, 0, color);
        var r = image.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.one * inset; r.offsetMax = -Vector2.one * inset;
        image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal;
        return image;
    }
    void Awake()
    {
        Build(); SyncLocalPlayer();
        if (hideLegacyPlayerHud)
            foreach (var legacy in FindObjectsByType<PlayerHudUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!legacy.transform.IsChildOf(transform)) legacy.gameObject.SetActive(false);
    }
    void OnEnable()
    {
        if (playerState != null) { playerState.HpChanged -= RefreshHp; playerState.HpChanged += RefreshHp; RefreshHp(playerState.CurrentHp, playerState.MaxHp); }
    }
    void OnDisable() { if (playerState != null) playerState.HpChanged -= RefreshHp; }
    void Update()
    {
        SyncLocalPlayer(); RefreshTimer(); RefreshStamina(); RefreshSkills(); RefreshStatuses(); RefreshStorm(); ApplyLayout(Screen.safeArea, Screen.width, Screen.height);
        if (Time.unscaledTime >= damageHold) trailingHp = Mathf.MoveTowards(trailingHp, hpRatio, Time.unscaledDeltaTime * .65f);
        if (hpTrail != null) hpTrail.fillAmount = trailingHp;
        if (Time.unscaledTime < nextLateBindAt) return;
        nextLateBindAt = Time.unscaledTime + .3f;
        string nickname = LocalIdentity.Instance != null ? LocalIdentity.Instance.PlayerName : null;
        playerNameText.text = !string.IsNullOrWhiteSpace(nickname) ? nickname : player != null ? player.PlayerId : "PLAYER";
        var team = PlayerNameplate.LocalTeam >= 0 ? TeamPalette.Get(PlayerNameplate.LocalTeam) : T.White;
        playerNameText.color = team; teamMark.color = team;
        var ab = ABClient.Active;
        levelText.text = $"LV. {(ab != null && ab.Supported ? ab.State.level : 1):D2}";
        if (playerState != null)
        {
            var portrait = PlayerPortrait.Ensure(playerState.transform);
            if (portrait != null && portrait.Texture != null) { portraitRaw.texture = portrait.Texture; portraitRaw.color = Color.white; }
        }
    }
    void SyncLocalPlayer()
    {
        if (bootstrap == null) bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        var next = bootstrap != null && bootstrap.LocalPlayer != null ? bootstrap.LocalPlayer.GetComponent<Player_State>() : null;
        if (bootstrap == null) next = playerState;
        if (next == playerState) return;
        if (playerState != null) playerState.HpChanged -= RefreshHp;
        playerState = next; player = next != null ? next.GetComponent<Player>() : null;
        portraitRaw.texture = null; portraitRaw.color = Color.clear;
        if (next != null) { next.HpChanged += RefreshHp; trailingHp = hpRatio = next.CurrentHp / (float)Mathf.Max(1, next.MaxHp); RefreshHp(next.CurrentHp, next.MaxHp); }
    }
    void RefreshHp(int current, int maximum)
    {
        int max = Mathf.Max(1, maximum), value = Mathf.Clamp(current, 0, max);
        float ratio = value / (float)max;
        if (ratio < hpRatio) damageHold = Time.unscaledTime + .45f; else if(ratio>hpRatio) trailingHp = ratio;
        hpRatio = ratio;
        if (hpFill == null) return;
        hpFill.fillAmount = ratio;
        hpFill.color = ratio <= .25f ? new Color(.97f, .32f, .30f) : new Color(.76f, .25f, .29f);
        healthAccent.color = ratio <= .25f ? hpFill.color : T.Gold;
        hpText.text = $"{value} / {max}";
    }
    void RefreshStamina()
    {
        var stamina = PlayerStamina.Local;
        if (stamina != null) { maxStamina = stamina.Max; currentStamina = stamina.Current; }
        int max = Mathf.Max(1, maxStamina), value = Mathf.Clamp(currentStamina, 0, max);
        if (staminaFill != null) staminaFill.fillAmount = value / (float)max;
        if (staminaText != null) staminaText.text = $"스태미나  {value} / {max}";
    }
    void RefreshSkills()
    {
        var skills = SkillClient.Active;
        bool supported = skills != null && skills.Supported;
        bool usable = supported && playerState != null && !playerState.IsDead && !playerState.IsHit
            && playerState.GetComponent<Player_Attack>()?.IsSwordEquipped == true
            && (ABClient.Active == null || (ABClient.Active.State.statusMask & 4) == 0);
        bool busy = playerState != null && (PlayerSkillView.Busy(playerState.gameObject) || playerState.GetComponent<PlayerDefenseController>()?.IsBusy == true);
        Color skillTint = SkillPalette.ForWeapon(ABClient.Active != null ? ABClient.Active.State.weapon : 0);
        eCard.SetTint(skillTint); qCard.SetTint(skillTint);
        bool great=playerState!=null&&playerState.GetComponent<Player_Attack>()?.IsGreatswordEquipped==true;
        eCard.SetTitle(great?"회전 베기":"검기");qCard.SetTitle(great?"천공의 대검":"전방 검격");
        eCard.Refresh(supported ? skills.ECooldown : 0, supported ? skills.EDuration : 8, supported, usable && !busy && !SkillClient.HasPendingCast, supported ? skills.BuffLeft : 0);
        qCard.Refresh(supported ? skills.QCooldown : 0, supported ? skills.QDuration : 20, supported, usable && !busy && !SkillClient.HasPendingCast, supported ? skills.BuffLeft : 0);
    }
    void RefreshStatuses()
    {
        int mask=playerState==null||playerState.IsDead?0:ABClient.Active!=null&&ABClient.Active.Supported?ABClient.Active.State.statusMask:playerState.GetComponent<MummyPoison>()!=null?8:0;
        for(int i=0;i<4;i++)debuffBadges[i].SetActive((mask&(1<<i))!=0);
    }
    void RefreshStorm()
    {
        var storm=bootstrap!=null?bootstrap.GetComponent<StormClient>():null;
        stormLabel.text=storm!=null&&storm.HasState?storm.StatusText:"자기장 정보 대기 중";
        stormValue.text=storm!=null&&storm.HasState?storm.CountdownText:"—";
        stormLabel.color=stormProgress.color=storm!=null&&storm.IsOutside?new Color(1,.48f,.32f):T.Gold;
        stormProgress.rectTransform.sizeDelta=new Vector2(280*(storm!=null&&storm.HasState?storm.Progress:0),3);
    }
    public void ApplyLayout(Rect safeArea, float width, float height)
    {
        if (root == null || width <= 0 || height <= 0) return;
        root.anchorMin = new Vector2(safeArea.xMin / width, safeArea.yMin / height);
        root.anchorMax = new Vector2(safeArea.xMax / width, safeArea.yMax / height);
        playerPanel.localScale = Vector3.one * Mathf.Min(1, root.rect.width / 1600f);
        timerPanel.localScale=Vector3.one*Mathf.Min(1,root.rect.width/580f);
    }
    void RefreshTimer()
    {
        bool inSession = bootstrap != null && bootstrap.Identity != null && bootstrap.Identity.IsConnectedToSession && bootstrap.SessionGameStarted;
        bool escaped = DungeonExitManager.Instance != null && DungeonExitManager.Instance.EscapeCompleted;
        if (!inSession) { timerRunning = clockFinished = false; frozenSeconds = 0; }
        else if (escaped) { if (timerRunning) frozenSeconds = Time.unscaledTime - runStartTime; timerRunning = false; clockFinished = true; }
        else if (!timerRunning && !clockFinished) { timerRunning = true; runStartTime = Time.unscaledTime; }
        var elapsed = TimeSpan.FromSeconds(Mathf.Max(0, timerRunning ? Time.unscaledTime - runStartTime : frozenSeconds));
        timerText.text = elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
