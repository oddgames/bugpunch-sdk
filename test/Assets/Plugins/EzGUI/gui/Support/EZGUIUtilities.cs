//-----------------------------------------------------------------
//  EZ GUI Utilities
//  Contains extension methods and utility classes needed for the
//  EZ GUI assembly that were previously sourced from ODDFramework.
//-----------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EZGUI
{
    /// <summary>
    /// Extension methods for Transform used by EZ GUI.
    /// </summary>
    public static class TransformExtensions
    {
        /// <summary>
        /// Gets the combined bounds of all renderers on this transform and its children.
        /// </summary>
        public static Bounds Bounds(this Transform obj, bool enabledOnly = false, bool includeZeroSize = true)
        {
            var renderers = ListPool<Renderer>.Get();
            try
            {
                obj.GetComponentsInChildren<Renderer>(true, renderers);

                int count = renderers.Count;

                if (count > 0)
                {
                    Bounds totalBounds = default;
                    bool set = false;

                    foreach (var r in renderers)
                    {
                        if (r is ParticleSystemRenderer)
                            continue;

                        if (!enabledOnly || (r.enabled && r.gameObject.activeInHierarchy))
                        {
                            if (!set)
                            {
                                totalBounds = r.bounds;
                                set = true;
                            }
                            else
                            {
                                totalBounds.Encapsulate(r.bounds);
                            }
                        }
                    }

                    if (set)
                        return totalBounds;
                }

                return new Bounds(obj.transform.position, Vector3.zero);
            }
            finally
            {
                ListPool<Renderer>.Release(ref renderers);
            }
        }

        /// <summary>
        /// Gets the hierarchy path of a transform.
        /// </summary>
        public static void GetHierarchyPath(this Transform transform, StringBuilder stringBuilder, Transform relativeTo = null, string separator = "/")
        {
            _ = stringBuilder ?? throw new ArgumentNullException(nameof(stringBuilder));

            var stopTransform = relativeTo != null
                ? relativeTo.parent
                : null;

            var pathList = ListPool<Component>.Get();

            try
            {
                while (transform != null && transform != stopTransform)
                {
                    pathList.Add(transform);
                    transform = transform.parent;
                }

                for (int x = pathList.Count - 1; x >= 0; x--)
                {
                    stringBuilder.Append(separator);
                    stringBuilder.Append(pathList[x].name);
                }
            }
            finally { ListPool<Component>.Release(ref pathList); }
        }

        /// <summary>
        /// Gets the hierarchy path of a transform as a string.
        /// </summary>
        public static string GetHierarchyPath(this Transform transform, Transform relativeTo = null, string separator = "/")
        {
            var builder = StringBuilderCache.Acquire();

            string result;
            try
            {
                GetHierarchyPath(transform, builder, relativeTo, separator);
                result = builder.ToString();
            }
            finally { StringBuilderCache.Release(builder); }

            return result;
        }
    }

    /// <summary>
    /// Simple object pool for reusing objects.
    /// </summary>
    public class ObjectPool<T> : IDisposable where T : class
    {
        private readonly Stack<T> m_Stack = new Stack<T>();
        private readonly Func<T> m_ActionOnGet;
        private readonly Action<T> m_ActionOnRelease;
        private readonly Action<T> m_ActionOnDispose;
        private int m_countAll;

        public int CountAll => m_countAll;
        public int CountInactive => this.m_Stack.Count;
        public int CountActive => this.CountAll - this.CountInactive;

        public ObjectPool(Func<T> actionOnGet, Action<T> actionOnRelease = null, Action<T> actionOnDispose = null)
        {
            this.m_ActionOnGet = actionOnGet;
            this.m_ActionOnRelease = actionOnRelease;
            this.m_ActionOnDispose = actionOnDispose;
        }

        public T Get()
        {
            T t;
            if (this.m_Stack.Count == 0)
            {
                t = this.m_ActionOnGet();
                this.m_countAll++;
            }
            else
            {
                t = this.m_Stack.Pop();
            }

            return t;
        }

        public void Release(ref T element)
        {
            if (element is null)
                return;

            if (this.m_Stack.Count > 0 && object.ReferenceEquals(this.m_Stack.Peek(), element))
            {
                Debug.LogError("Internal error. Trying to destroy object that is already released to pool.");
            }

            this.m_ActionOnRelease?.Invoke(element);
            this.m_Stack.Push(element);
            element = null;
        }

        public void Dispose()
        {
            var disposeAction = m_ActionOnDispose;
            if (disposeAction is not null)
            {
                foreach (T t in m_Stack)
                    disposeAction.Invoke(t);
            }

            m_Stack.Clear();
        }
    }

    /// <summary>
    /// Pool for reusing List instances.
    /// </summary>
    public static class ListPool<T>
    {
        private static readonly ObjectPool<List<T>> s_listPool;

        private static void Clear(List<T> list)
        {
            list.Clear();
        }

        public static List<T> Get()
        {
            return s_listPool.Get();
        }

        public static List<T> Get(int capacity)
        {
            var list = s_listPool.Get();

            if (list.Capacity < capacity)
                list.Capacity = capacity;

            return list;
        }

        public static void Release(ref List<T> list)
        {
            s_listPool.Release(ref list);
        }

        static ListPool()
        {
            s_listPool = new ObjectPool<List<T>>(() => new List<T>(), new Action<List<T>>(Clear));
        }
    }

    /// <summary>
    /// Provides a cached reusable instance of StringBuilder per thread.
    /// </summary>
    public static class StringBuilderCache
    {
        private const int MAX_BUILDER_SIZE = 360;
        private const int DEFAULT_BUILDER_CAPACITY = 16;

        [ThreadStatic]
        private static StringBuilder CachedInstance;

        public static StringBuilder Acquire(int capacity = DEFAULT_BUILDER_CAPACITY)
        {
            if (capacity <= MAX_BUILDER_SIZE)
            {
                StringBuilder sb = StringBuilderCache.CachedInstance;
                if (sb != null)
                {
                    if (capacity <= sb.Capacity)
                    {
                        StringBuilderCache.CachedInstance = null;
                        sb.Clear();
                        return sb;
                    }
                }
            }
            return new StringBuilder(capacity);
        }

        public static void Release(StringBuilder sb)
        {
            if (sb.Capacity <= MAX_BUILDER_SIZE)
            {
                StringBuilderCache.CachedInstance = sb;
            }
        }

        public static string GetStringAndRelease(StringBuilder sb)
        {
            string result = sb.ToString();
            Release(sb);
            return result;
        }
    }
}
