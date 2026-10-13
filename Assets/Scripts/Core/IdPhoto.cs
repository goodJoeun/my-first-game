using UnityEngine;

// 신분증 사진을 그린다 (그림 에셋 없이 픽셀로).
// 정면 사진이라 손님의 왼쪽은 사진의 오른쪽에 그려진다. 3D 매장에서 마주 본 손님 얼굴과 같은 방향이다.
public static class IdPhoto
{
    const int W = 96, H = 120;

    public static Texture2D Draw(IdCard card, bool glasses, string sex)
    {
        var px = new Color[W * H];
        var bg = new Color(0.78f, 0.84f, 0.9f);
        for (int i = 0; i < px.Length; i++) px[i] = bg;

        var skin = new Color(0.93f, 0.8f, 0.68f);
        var hair = new Color(0.12f, 0.1f, 0.09f);
        var dark = new Color(0.15f, 0.12f, 0.1f);
        var shirt = sex == "f" ? new Color(0.35f, 0.38f, 0.5f) : new Color(0.25f, 0.27f, 0.32f);

        // 어깨·목
        Ellipse(px, 48, 122, 44, 22, shirt);
        Rect(px, 41, 82, 14, 18, skin * 0.95f);
        // 긴 머리는 얼굴 뒤로 늘어진다
        if (card.hair == "long") Ellipse(px, 48, 66, 32, 40, hair);
        // 얼굴
        Ellipse(px, 48, 55, 25, 32, skin);
        // 윗머리
        Ellipse(px, 48, 26, 27, 12, hair);   // 이마가 보이게 (눈썹 위 점)
        if (card.hair == "long") { Rect(px, 22, 30, 7, 40, hair); Rect(px, 67, 30, 7, 40, hair); }
        // 눈썹·눈·코·입
        Rect(px, 33, 44, 11, 2, dark); Rect(px, 52, 44, 11, 2, dark);
        Ellipse(px, 38, 51, 3, 2, dark); Ellipse(px, 58, 51, 3, 2, dark);
        Rect(px, 47, 56, 2, 8, skin * 0.85f);
        Rect(px, 42, 70, 12, 2, new Color(0.6f, 0.3f, 0.3f));

        // 점·흉터 (손님의 왼쪽 → 사진의 오른쪽)
        if (card.marks != null)
            foreach (var m in card.marks)
            {
                switch (m)
                {
                    case "mole_left_cheek": Ellipse(px, 64, 62, 2, 2, dark); break;
                    case "mole_right_cheek": Ellipse(px, 32, 62, 2, 2, dark); break;
                    case "mole_left_brow": Ellipse(px, 59, 40, 2, 2, dark); break;
                    case "mole_right_brow": Ellipse(px, 37, 40, 2, 2, dark); break;
                    case "mole_chin": Ellipse(px, 48, 80, 2, 2, dark); break;
                    case "scar_left_brow": Line(px, 55, 47, 61, 40, new Color(0.65f, 0.35f, 0.35f)); break;
                    case "scar_right_brow": Line(px, 41, 47, 35, 40, new Color(0.65f, 0.35f, 0.35f)); break;
                }
            }

        if (glasses)
        {
            Frame(px, 31, 47, 14, 9, dark);
            Frame(px, 51, 47, 14, 9, dark);
            Rect(px, 45, 50, 6, 1, dark);
        }

        // 오래된 사진: 빛바랜 누런 색
        if (card.old_photo)
            for (int i = 0; i < px.Length; i++)
            {
                float g = px[i].grayscale;
                px[i] = Color.Lerp(new Color(g * 1.05f, g * 0.95f, g * 0.75f), Color.white, 0.25f);
            }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        // 위에서 아래로 그린 것을 텍스처 좌표(아래가 0)로 뒤집는다
        var flipped = new Color[px.Length];
        for (int y = 0; y < H; y++)
            System.Array.Copy(px, y * W, flipped, (H - 1 - y) * W, W);
        tex.SetPixels(flipped);
        tex.Apply();
        return tex;
    }

    static void Set(Color[] px, int x, int y, Color c)
    {
        if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c;
    }

    static void Rect(Color[] px, int x, int y, int w, int h, Color c)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++) Set(px, i, j, c);
    }

    static void Frame(Color[] px, int x, int y, int w, int h, Color c)
    {
        Rect(px, x, y, w, 1, c); Rect(px, x, y + h - 1, w, 1, c);
        Rect(px, x, y, 1, h, c); Rect(px, x + w - 1, y, 1, h, c);
    }

    static void Ellipse(Color[] px, int cx, int cy, int rx, int ry, Color c)
    {
        for (int j = cy - ry; j <= cy + ry; j++)
            for (int i = cx - rx; i <= cx + rx; i++)
            {
                float dx = (i - cx) / (float)rx, dy = (j - cy) / (float)ry;
                if (dx * dx + dy * dy <= 1f) Set(px, i, j, c);
            }
    }

    static void Line(Color[] px, int x0, int y0, int x1, int y1, Color c)
    {
        int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        for (int k = 0; k <= n; k++)
        {
            int x = x0 + (x1 - x0) * k / Mathf.Max(1, n), y = y0 + (y1 - y0) * k / Mathf.Max(1, n);
            Set(px, x, y, c); Set(px, x + 1, y, c);
        }
    }
}
