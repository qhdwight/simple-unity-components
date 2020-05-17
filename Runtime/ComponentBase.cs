using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Swihoni.Components
{
    public abstract class ElementBase
    {
        private bool Equals(ElementBase other) { return this.EqualTo(other); }

        public override bool Equals(object other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return other.GetType() == GetType() && Equals((ElementBase) other);
        }

        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    /// <summary>
    /// Stores a collection of elements. Access via <see cref="Elements"/> or with indexer.
    /// Once you add an element, you need to make sure it is "owned" by the component (effectively moving it).
    /// A conscious choice was made to use classes instead of structs, so proper responsibility has to be taken.
    /// What this means in practice is, unless you know exactly what you are doing, once you <see cref="Append"/>
    /// </summary>
    public abstract class ComponentBase : ElementBase
    {
        private List<ElementBase> m_Elements;

        public IReadOnlyList<ElementBase> Elements
        {
            get
            {
                VerifyFieldsRegistered();
                return m_Elements;
            }
        }

        public ElementBase this[int index] => Elements[index];

        private void VerifyFieldsRegistered()
        {
            if (m_Elements != null) return;

            m_Elements = new List<ElementBase>();
            IReadOnlyList<FieldInfo> fieldInfos = Cache.GetFieldInfo(GetType());
            foreach (FieldInfo field in fieldInfos)
            {
                object fieldValue = field.GetValue(this);
                if (fieldValue is ElementBase element) Append(element);
            }
        }

        protected void ClearRegistered() => m_Elements = new List<ElementBase>();

        protected ComponentBase() => InstantiateFieldElements();

        private void InstantiateFieldElements()
        {
            foreach (FieldInfo field in Cache.GetFieldInfo(GetType()))
            {
                Type fieldType = field.FieldType;
                bool isElement = fieldType.IsElement();
                if (!isElement || fieldType.IsAbstract || field.GetValue(this) != null) continue;

                object instance = Activator.CreateInstance(fieldType);
                if (instance is PropertyBase propertyInstance) propertyInstance.Field = field;
                field.SetValue(this, instance);
            }
        }

        /// <summary>
        /// Appends an element to the end of this component.
        /// To be able to retrieve by type, consider using a <see cref="Container"/>.
        /// For registering an element with a component, you wil need to remember the index.
        /// </summary>
        /// <returns>Index of element</returns>
        protected virtual int Append(ElementBase element)
        {
            VerifyFieldsRegistered();
            m_Elements.Add(element);
            return m_Elements.Count - 1;
        }

        /// <summary>
        /// Called during interpolation. Use to add custom behavior.
        /// </summary>
        public virtual void InterpolateFrom(ComponentBase c1, ComponentBase c2, float interpolation) { }
    }
}