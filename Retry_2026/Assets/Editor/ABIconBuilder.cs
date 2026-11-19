using System.IO;
using UnityEditor;
using UnityEngine;

// Small, code-drawn glyphs. No downloaded artwork or additional runtime textures.
public static class ABIconBuilder
{
    public static Sprite Get(ABItemDefinitions.Item item)
    {
        if (item.kind == 2 && WeaponIconBuilder.Get(item.effect) is var modelIcon && modelIcon != null) return modelIcon;
        string folder = "Assets/CombatContent/Items/AB/Icons";
        Directory.CreateDirectory(folder);
        string path = folder + "/" + (item.kind == 2 ? "weapon_" : "item_") + item.effect + ".png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color tint = Color.HSVToRGB((item.effect * .091f + .01f) % 1, .58f, .95f);
            if (item.kind == 2)
                tint = item.WeaponElement == 1   ? new Color(1, .25f, .06f)
                       : item.WeaponElement == 2 ? new Color(.2f, .7f, 1)
                                          : new Color(.3f, 1, .6f);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float u = (x - 31.5f) / 32, v = (y - 31.5f) / 32, r = Mathf.Sqrt(u * u + v * v);
                    Color c = r > .94f   ? Color.clear
                              : r > .85f ? new Color(.42f, .48f, .55f)
                                         : new Color(.055f, .075f, .11f);
                    bool shape = item.kind == 2 ? Sword(item.IsGreatsword ? u * .72f : u, v) : Glyph(item.effect, u, v);
                    if (shape)
                        c = Color.Lerp(tint, Color.white, Mathf.Clamp01((v + .5f) * .3f));
                    texture.SetPixel(x, y, c);
                }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 64;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        var settings=(TextureImporter)AssetImporter.GetAtPath(path);
        if(settings.textureType!=TextureImporterType.Sprite || settings.spriteImportMode!=SpriteImportMode.Single){
            settings.textureType=TextureImporterType.Sprite;settings.spriteImportMode=SpriteImportMode.Single;settings.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static bool Rect(float x, float y, float cx, float cy, float w, float h) => Mathf.Abs(x - cx) < w
                                                                                && Mathf.Abs(y - cy) < h;
    static bool Line(float x, float y, float ax, float ay, float bx, float by, float width)
    {
        var p = new Vector2(x - ax, y - ay);
        var d = new Vector2(bx - ax, by - ay);
        float t = Mathf.Clamp01(Vector2.Dot(p, d) / d.sqrMagnitude);
        return (p - t * d).sqrMagnitude < width * width;
    }
    static bool Sword(float x, float y) => Line(x, y, -.4f, -.55f, .45f, .6f, .085f) ||
                                           Line(x, y, -.5f, -.12f, .02f, -.5f, .07f);
    static bool Glyph(int effect, float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        if (effect < 4)
        {
            bool bottle =
                Rect(x, y, 0, -.15f, .32f, .37f) || Rect(x, y, 0, .32f, .14f, .16f) || Rect(x, y, 0, .5f, .21f, .08f);
            bool mark = effect == 0   ? (Mathf.Abs(x) < .065f || Mathf.Abs(y + .14f) < .06f)
                        : effect == 1 ? Line(x, y, -.2f, -.3f, .2f, .05f, .055f)
                        : effect == 2 ? Mathf.Abs(x) + Mathf.Abs(y + .15f) < .13f
                                      : r < .13f;
            return bottle && !(mark && y < .2f);
        }
        switch (effect)
        {
        case 16: // Hourglass
            return Rect(x,y,0,.52f,.38f,.06f)||Rect(x,y,0,-.52f,.38f,.06f)||
                Line(x,y,-.3f,-.45f,.3f,.45f,.05f)||Line(x,y,.3f,-.45f,-.3f,.45f,.05f)||
                y<-.18f&&y>-.43f&&Mathf.Abs(x)<(-y-.1f)*.65f;
        case 17: // Focusing lens
            return r<.46f&&r>.36f||Mathf.Abs(x)+Mathf.Abs(y)<.24f||Line(x,y,.3f,-.3f,.55f,-.55f,.075f);
        case 18: // Echo orb and return arc
            return r<.22f||r>.43f&&r<.51f&&x<.32f||Line(x,y,.29f,.36f,.53f,.3f,.07f)||Line(x,y,.29f,.36f,.35f,.59f,.07f);
        case 19: // Prism and expanding wave
            return Mathf.Abs(Mathf.Abs(x)+Mathf.Abs(y)*.65f-.30f)<.045f||
                r>.52f&&r<.57f&&Mathf.Abs(y)<.40f;
        case 20: // Clock inside a vial
        case 21: // Spark inside a vial
            bool bottle=Rect(x,y,0,-.15f,.34f,.37f)||Rect(x,y,0,.32f,.14f,.16f)||Rect(x,y,0,.5f,.21f,.08f);
            bool mark=effect==20 ? Mathf.Abs(new Vector2(x,y+.13f).magnitude-.18f)<.04f||Line(x,y,0,-.13f,0,.01f,.035f)
                : Mathf.Abs(x)+Mathf.Abs(y+.13f)*.65f<.18f;
            return bottle&&!mark;
        case 4:
            return (new Vector2(x - .23f, y - .2f).magnitude < .28f ||
                    new Vector2(x + .23f, y - .2f).magnitude < .28f ||
                    y < .2f && y > -.55f && Mathf.Abs(x) < (y + .55f) * .7f);
        case 5:
            return r < .5f && r > .31f || Mathf.Abs(x) + Mathf.Abs(y - .47f) < .19f;
        case 6:
            return (x + y) * (x + y) * 1.4f + (x - y) * (x - y) * .45f < .32f && Mathf.Abs(x - y) > .04f;
        case 7:
            return y < .45f && y > -.55f &&
                   (Mathf.Abs(x - .23f) < (y + .55f) * .18f || Mathf.Abs(x + .23f) < (y + .55f) * .18f);
        case 8:
            return r < .5f && r > .4f || r < .19f || Rect(x, y, 0, 0, .055f, .65f) || Rect(x, y, 0, 0, .65f, .055f);
        case 9:
            return Sword(x, y) || Sword(-x, y);
        case 10:
            return y > -.5f && y < .65f && Mathf.Abs(x) < .42f * (1 - (y + .5f) / 1.15f) &&
                   !(y < -.05f && Mathf.Abs(x) < .13f);
        case 11:
            return Line(x, y, -.55f, 0, .55f, 0, .055f) || Line(x, y, -.3f, -.52f, .3f, .52f, .055f) ||
                   Line(x, y, -.3f, .52f, .3f, -.52f, .055f);
        case 12:
            return Line(x, y, -.4f, -.5f, .4f, .5f, .065f) || Line(x, y, -.12f, -.1f, -.4f, .18f, .08f) ||
                   Line(x, y, .17f, .22f, .48f, .14f, .08f);
        case 13:
            return y > 0 && y < .65f && x > -.3f && x < .22f && x < y * .6f ||
                   y < .1f && y > -.65f && x > -.22f && x < .3f && x > y * .6f;
        case 14:
            return Mathf.Abs(x) < .57f &&
                   Mathf.Abs(y) < .38f + (x > .18f    ? .12f
                                          : x < -.18f ? .08f
                                                      : 0) &&
                   Mathf.Abs(Mathf.Abs(x) - .18f) > .035f;
        default:
            return Mathf.Abs(x) < .48f && y < .5f && y > -.6f + Mathf.Abs(x) * .7f &&
                   !(Mathf.Abs(x) < .055f || Mathf.Abs(y - .1f) < .055f);
        }
    }
}
