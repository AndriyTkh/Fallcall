using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using UnityEngine;

namespace OsuUnity.Visual
{
    /// <summary>Base type for the on-screen representation of a hit object.</summary>
    public abstract class DrawableHitObject : MonoBehaviour
    {
        protected GameContext Ctx;
        public HitObject Object { get; private set; }

        /// <summary>Object is fully resolved (judged + animation done) and can be destroyed.</summary>
        public bool Finished { get; protected set; }

        /// <summary>Head/circle judged: the gameplay queue may advance the "front" pointer past it.</summary>
        public bool HeadJudged { get; protected set; }

        public virtual void Init(HitObject ho, GameContext ctx)
        {
            Object = ho;
            Ctx = ctx;
        }

        /// <param name="time">current song time in ms.</param>
        /// <param name="isFront">true if this is the front-most un-judged object (note lock).</param>
        public abstract void Tick(double time, bool isFront);

        protected static SpriteRenderer AddSprite(Transform parent, Sprite sprite, Color colour,
            float diameter, int order)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = colour;
            sr.sortingOrder = order;
            go.transform.localScale = Vector3.one * diameter;
            return sr;
        }

        protected static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr == null) return;
            Color c = sr.color;
            c.a = a;
            sr.color = c;
        }
    }
}
