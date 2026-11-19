using UnityEngine;
using UnityEngine.UI;
using T = ExpeditionTheme;

/// <summary>Read-only status; input and cooldown authority stay in SkillClient.</summary>
public sealed class SkillHudCard : MonoBehaviour
{
    Image accent, cooldownMask, progress;
    Text status, countdown, titleLabel;
    public void SetTitle(string title){if(titleLabel!=null)titleLabel.text=title;}
    SkillHudGlyph glyph;
    Color tint;
    public void SetTint(Color color) { tint = color; }
    public static SkillHudCard Create(Transform parent, string key, string title, int skill, Vector2 position)
    {
        var surface = T.Box(key + " Skill", parent, position.x, position.y, 196, 76, T.Ink);
        var card = surface.gameObject.AddComponent<SkillHudCard>();
        card.tint = skill == 1 ? new Color(.46f, .78f, .85f) : T.Gold;
        card.accent = T.Box("Accent", surface.transform, -97, 0, 2, 46, card.tint);
        var icon = T.Box("Icon Well", surface.transform, -62, 0, 52, 52, T.Card);
        card.glyph = T.Rect("Skill Glyph", icon.transform, 0, 0, 42, 42).gameObject.AddComponent<SkillHudGlyph>();
        card.glyph.Skill = skill; card.glyph.color = card.tint; card.glyph.raycastTarget = false;
        card.cooldownMask = T.Box("Cooldown Sweep", icon.transform, 0, 0, 52, 52, new Color(.015f, .024f, .035f, .82f));
        card.cooldownMask.type = Image.Type.Filled; card.cooldownMask.fillMethod = Image.FillMethod.Radial360;
        card.cooldownMask.fillOrigin = 2; card.cooldownMask.fillClockwise = true;
        card.countdown = T.Label("Cooldown", icon.transform, 0, 0, 48, 34, 22, T.White, "", TextAnchor.MiddleCenter);
        var keycap = T.Box("Keycap", icon.transform, -20, -21, 21, 22, new Color(.16f, .19f, .22f));
        T.Label("Key", keycap.transform, 0, 0, 21, 22, 12, T.White, key, TextAnchor.MiddleCenter);
        card.titleLabel=T.Label("Name", surface.transform, 28, 13, 106, 26, 17, T.White, title);
        card.status = T.Label("Status", surface.transform, 28, -10, 106, 20, 13, T.Muted, "준비");
        T.Box("Cooldown Track", surface.transform, 0, -37, 164, 2, T.Card);
        card.progress = T.Box("Cooldown Progress", surface.transform, 0, -37, 164, 2, card.tint);
        card.progress.type = Image.Type.Filled; card.progress.fillMethod = Image.FillMethod.Horizontal;
        card.Refresh(0, skill == 1 ? 8 : 25, false, false);
        return card;
    }
    public void Refresh(float seconds, float duration, bool supported, bool usable, float buffLeft = 0)
    {
        seconds = Mathf.Max(0, seconds);
        bool cooling = supported && seconds > .001f, ready = supported && !cooling && usable;
        cooldownMask.fillAmount = cooling ? Mathf.Clamp01(seconds / duration) : 0;
        countdown.text = cooling ? seconds < 1 ? seconds.ToString("0.0") : Mathf.CeilToInt(seconds).ToString() : "";
        status.text = !supported ? "대기 중" : cooling ? "재사용 대기" : ready ? "준비" : "사용 불가";
        if(supported && buffLeft > 0)status.text = "강화 " + Mathf.CeilToInt(buffLeft) + "초";
        status.color = ready ? tint : T.Muted;
        // Keep the countdown legible over the emblem, including the uncovered part of the sweep.
        glyph.color = cooling ? new Color(tint.r, tint.g, tint.b, .18f) : ready ? tint : Color.Lerp(T.Muted, tint, .3f);
        accent.color = ready ? tint : new Color(T.Muted.r, T.Muted.g, T.Muted.b, .35f);
        progress.fillAmount = supported ? 1 - Mathf.Clamp01(seconds / duration) : 0;
        progress.color = ready ? tint : new Color(tint.r, tint.g, tint.b, .45f);
    }
}

/// <summary>Vector sword-wave and sweeping-cut emblems, crisp at every canvas scale.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SkillHudGlyph : MaskableGraphic
{
    public SkillHudGlyph() { useLegacyMeshGeneration = false; }
    public int Skill { get; set; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r = rectTransform.rect;
        Vector2 P(float x, float y) => new Vector2(r.center.x + x * r.width, r.center.y + y * r.height);
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int n = vh.currentVertCount;
            foreach (var p in new[] { a, b, c, d }) vh.AddVert(p, color, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n + 2, n + 3, n);
        }
        void Stroke(Vector2 a, Vector2 b, float width)
        {
            Vector2 normal = new Vector2(-(b-a).y, (b-a).x).normalized * width * r.width * .5f;
            Quad(a+normal, b+normal, b-normal, a-normal);
        }
        Stroke(P(-.28f,-.28f), P(.29f,.29f), .065f);
        Quad(P(.17f,.26f), P(.35f,.39f), P(.29f,.18f), P(.02f,-.08f));
        Stroke(P(-.26f,-.04f), P(-.04f,-.26f), .07f);
        if (Skill == 1)
        {
            for (int i = 0; i < 16; i++) {
                float a = Mathf.Lerp(-70, 70, i/16f) * Mathf.Deg2Rad, b = Mathf.Lerp(-70, 70, (i+1)/16f) * Mathf.Deg2Rad;
                Stroke(P(.31f*Mathf.Cos(a)-.02f, .42f*Mathf.Sin(a)), P(.31f*Mathf.Cos(b)-.02f, .42f*Mathf.Sin(b)), .045f);
            }
            Stroke(P(-.43f,.12f),P(-.24f,.12f),.035f); Stroke(P(-.38f,.24f),P(-.20f,.24f),.035f);
        }
        else
        {
            for (int i = 0; i < 20; i++) {
                float a = Mathf.Lerp(185, 345, i/20f) * Mathf.Deg2Rad, b = Mathf.Lerp(185, 345, (i+1)/20f) * Mathf.Deg2Rad;
                Stroke(P(.44f*Mathf.Cos(a), .28f*Mathf.Sin(a)-.02f), P(.44f*Mathf.Cos(b), .28f*Mathf.Sin(b)-.02f), .06f);
            }
            Stroke(P(-.35f,.30f),P(-.35f,.45f),.035f); Stroke(P(-.425f,.375f),P(-.275f,.375f),.035f);
        }
    }
}
