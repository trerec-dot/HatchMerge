using UnityEngine;
using UnityEngine.UI;

// Искорка при слиянии
public class Spark : MonoBehaviour
{
    RectTransform rt;
    Image im;
    Vector2 vel;
    Color col;
    float life = 1f;

    public void Init(Image img, Color c, Vector2 v) { im = img; rt = (RectTransform)transform; col = c; vel = v; }

    void Update()
    {
        life -= Time.deltaTime * 2.2f;
        if (life <= 0f) { Destroy(gameObject); return; }
        rt.anchoredPosition += vel * Time.deltaTime;
        vel *= 1f - 3f * Time.deltaTime;
        im.color = new Color(col.r, col.g, col.b, life);
        rt.localScale = Vector3.one * life;
    }
}

// Всплывающий текст очков
public class Floater : MonoBehaviour
{
    RectTransform rt;
    Text tx;
    Vector2 start;
    float t;

    public void Init(Text text) { tx = text; rt = (RectTransform)transform; start = rt.anchoredPosition; }

    void Update()
    {
        t += Time.deltaTime;
        rt.anchoredPosition = start + Vector2.up * (46f * Mathf.Clamp01(t / 0.9f));
        var c = tx.color;
        c.a = 1f - Mathf.Clamp01((t - 0.4f) / 0.5f);
        tx.color = c;
        if (t > 0.95f) Destroy(gameObject);
    }
}
