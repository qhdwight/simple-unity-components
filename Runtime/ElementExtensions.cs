using System;

namespace Swihoni.Components
{
    public enum Navigation
    {
        Continue,
        SkipDescendents, // Skip all children of current node
        Exit             // Exit entire tree immediately
    }

    public static class ElementExtensions
    {
        public static bool IsElement(this Type type) => type.IsSubclassOf(typeof(ElementBase));

        /// <summary>
        /// Un-sets with value flag.
        /// If you instead want to zero, see <see cref="Zero{T}"/>
        /// </summary>
        public static void Reset(this ElementBase element)
        {
            element.Navigate(_e =>
            {
                (_e as PropertyBase)?.Clear();
                return Navigation.Continue;
            });
        }

        /// <summary>
        /// Reset all properties to default values.
        /// Sets with value flags as well.
        /// </summary>
        public static T Zero<T>(this T element) where T : ElementBase
        {
            element.Navigate(_e =>
            {
                (_e as PropertyBase)?.Zero();
                return Navigation.Continue;
            });
            return element;
        }

        /// <summary>
        /// Allocates a cloned instance. Do not use in loops.
        /// </summary>
        public static TElement Clone<TElement>(this TElement element) where TElement : ElementBase
        {
            var clone = (TElement) Activator.CreateInstance(element.GetType());
            NavigateZipped((_e1, _e2) =>
            {
                if (_e1 is Container p1 && _e2 is Container p2)
                    p2.TakeElementTypes(p1);
                return Navigation.Continue;
            }, element, clone);
            clone.MergeFrom(element);
            return clone;
        }

        /// <summary>
        /// Test if all properties are equal. Uses <see cref="PropertyBase.Equals(PropertyBase)"/> for comparision.
        /// </summary>
        public static bool EqualTo<T>(this T e1, T e2) where T : ElementBase
        {
            var areEqual = true;
            NavigateZipped((_e1, _e2) =>
            {
                if (_e1 is PropertyBase p1 && _e2 is PropertyBase p2)
                {
                    if (p1.Equals(p2))
                        return Navigation.Continue;
                    areEqual = false;
                    return Navigation.Exit;
                }
                return Navigation.Continue;
            }, e1, e2);
            return areEqual;
        }

        /// <summary>See: <see cref="Navigate"/></summary>
        public static void Navigate(this ElementBase e, Func<ElementBase, Navigation> visit)
        {
            var zip = new TriArray<ElementBase> {[0] = e};
            Navigate(_zip => visit(_zip[0]), zip, 1);
        }

        /// <summary>See: <see cref="Navigate"/></summary>
        public static void NavigateZipped(Func<ElementBase, ElementBase, Navigation> visit, ElementBase e1, ElementBase e2)
        {
            var zip = new TriArray<ElementBase> {[0] = e1, [1] = e2};
            Navigate(_zip => visit(_zip[0], _zip[1]), zip, 2);
        }

        /// <summary>See: <see cref="Navigate"/></summary>
        public static void NavigateZipped(Func<ElementBase, ElementBase, ElementBase, Navigation> visit, ElementBase e1, ElementBase e2, ElementBase e3)
        {
            var zip = new TriArray<ElementBase> {[0] = e1, [1] = e2, [2] = e3};
            Navigate(_zip => visit(_zip[0], _zip[1], _zip[2]), zip, 3);
        }

        /// <summary>
        /// Iterates over all elements in a zipped fashion. Similar to the zip function in python.
        /// Important limitations: uses orders of elements for components and containers. It keeps iterates only if the next two elements are the same.
        /// This function needs to be performant since it is designed to be called in game update loop.
        /// </summary>
        /// <param name="visit">Called at each node in the "tree" of elements.</param>
        /// <param name="zip">Zipped element roots.</param>
        /// <param name="size">Amount of elements zipped to zip together. Max three supported.</param>
        /// <exception cref="ArgumentException">If an object navigated was not an element.</exception>
        private static void Navigate(Func<TriArray<ElementBase>, Navigation> visit, in TriArray<ElementBase> zip, int size)
        {
            if (size <= 0) throw new ArgumentException("Size needs to be greater than zero");
            var exitAll = false;
            void NavigateRecursively(in TriArray<ElementBase> _zip)
            {
                Navigation navigation = visit(_zip);
                if (navigation == Navigation.Exit)
                    exitAll = true;
                if (exitAll || navigation == Navigation.SkipDescendents)
                    return;
                switch (_zip[0])
                {
                    case Container _:
                    {
                        var zippedContainers = new TriArray<Container>();
                        // Truncate iteration to count of smallest elements collection
                        var elementSize = int.MaxValue;
                        for (var i = 0; i < size; i++)
                        {
                            zippedContainers[i] = (Container) _zip[i];
                            int count = zippedContainers[i].Elements.Count;
                            if (count < elementSize) elementSize = count;
                        }
                        // Truncate iteration if elements become different types
                        var foundDifferentType = false;
                        for (var j = 0; j < elementSize && !foundDifferentType; j++)
                        {
                            Type firstType = zippedContainers[0][j].GetType();
                            for (var i = 1; i < size && !foundDifferentType; i++)
                            {
                                if (zippedContainers[i][j].GetType() == firstType) continue;
                                elementSize = j;
                                foundDifferentType = true;
                            }
                        }

                        for (var j = 0; j < elementSize; j++)
                        {
                            var zippedChildren = new TriArray<ElementBase>();
                            for (var i = 0; i < size; i++) zippedChildren[i] = zippedContainers[i][j];
                            NavigateRecursively(zippedChildren);
                        }
                        break;
                    }
                    case ArrayElementBase a1:
                    {
                        for (var j = 0; j < a1.Length; j++)
                        {
                            var zippedElements = new TriArray<ElementBase>();
                            for (var i = 0; i < size; i++) zippedElements[i] = (ElementBase) ((ArrayElementBase) _zip[i]).GetValue(j);
                            NavigateRecursively(zippedElements);
                        }
                        break;
                    }
                    case ComponentBase c1:
                    {
                        for (var j = 0; j < c1.Elements.Count; j++)
                        {
                            var zippedChildren = new TriArray<ElementBase>();
                            for (var i = 0; i < size; i++) zippedChildren[i] = ((ComponentBase) _zip[i])[j];
                            NavigateRecursively(zippedChildren);
                        }
                        break;
                    }
                    default:
                    {
                        if (!(_zip[0] is PropertyBase)) throw new ArgumentException("Expected component or array");
                        break;
                    }
                }
            }
            NavigateRecursively(zip);
        }

        /// <summary>
        /// Avoids excessive heap allocation by providing a fixed size array.
        /// There is no easy way to create a fixed size array on the stack as of the current C# version for Unity.
        /// I don't like it either okay.
        /// </summary>
        private struct TriArray<T>
        {
            private T m_E1, m_E2, m_E3;

            public T this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:  return m_E1;
                        case 1:  return m_E2;
                        case 2:  return m_E3;
                        default: throw new IndexOutOfRangeException();
                    }
                }
                set
                {
                    // @formatter:off
                    switch (index)
                    {
                        case 0:  m_E1 = value; break;
                        case 1:  m_E2 = value; break;
                        case 2:  m_E3 = value; break;
                        default: throw new IndexOutOfRangeException();
                    }
                    // @formatter:on
                }
            }
        }
    }
}