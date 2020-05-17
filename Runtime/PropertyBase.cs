using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Swihoni.Components
{
    [Flags]
    public enum ElementFlags : byte
    {
        None,
        WithValue,
        DontSerialize
    }

    /// <summary>
    /// <see cref="PropertyBase{T}"/>
    /// </summary>
    [Serializable]
    public abstract class PropertyBase : ElementBase
    {
        [SerializeField] private ElementFlags m_Flags = ElementFlags.None;

        public FieldInfo Field { get; set; }

        public bool WithValue
        {
            get => (m_Flags & ElementFlags.WithValue) != 0;
            protected set
            {
                // TODO:refactor generalized methods for setting flags
                if (value) m_Flags |= ElementFlags.WithValue;
                else m_Flags &= ~ElementFlags.WithValue;
            }
        }

        public bool WithoutValue => !WithValue;

        public bool DontSerialize
        {
            get => (m_Flags & ElementFlags.DontSerialize) != 0;
            protected set
            {
                if (value) m_Flags |= ElementFlags.DontSerialize;
                else m_Flags &= ~ElementFlags.DontSerialize;
            }
        }

        public abstract void Serialize(BinaryWriter writer);
        public abstract void Deserialize(BinaryReader reader);
        public abstract bool Equals(PropertyBase other);
        public abstract void Clear();
        public abstract void Zero();
        public abstract void SetFromIfWith(PropertyBase other);
        public abstract void InterpolateFromIfWith(PropertyBase p1, PropertyBase p2, float interpolation);
    }

    public class WithoutValueException : Exception
    {
        public WithoutValueException(string message) : base(message) { }
    }

    /// <summary>
    /// Wrapper for holding a value.
    /// This is a class, so it is always passed by reference.
    /// This means that extra care needs to be taken with using properties.
    /// They should only ever belong to one container.
    /// They should never be null. Use the <see cref="PropertyBase.WithValue"/> feature instead.
    /// To set values, use <see cref="SetFromIfWith"/> or <see cref="Value"/>. Clear with <see cref="Clear"/>.
    /// Do not assign one property directly to another, as this replaces the reference instead of copying value!
    /// Equality operators are overriden to compare values instead of pointers.
    /// </summary>
    [Serializable]
    public abstract class PropertyBase<T> : PropertyBase where T : struct
    {
        [CopyField, SerializeField] private T m_Value;

        protected const float DefaultFloatTolerance = 1e-5f;

        /// <summary>
        /// Use only if this property is with a value.
        /// If you are unsure, use <see cref="PropertyBase.WithValue"/> or <see cref="IfWith"/>.
        /// </summary>
        /// <returns>Value wrapped by property.</returns>
        /// <exception cref="WithoutValueException">If without value.</exception>
        public T Value
        {
            get
            {
                if (WithValue)
                    return m_Value;
                throw new WithoutValueException($"No value for: {GetType().Name} attached to field: {Field?.Name ?? "None"}!");
            }
            set
            {
                m_Value = value;
                WithValue = true;
            }
        }

        public T? NullableValue => WithValue ? m_Value : (T?) null;

        protected PropertyBase() { }

        protected PropertyBase(T value) => Value = value;

        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

        public override bool Equals(object other)
        {
            if (ReferenceEquals(this, other)) return true;
            var otherProperty = (PropertyBase) other;
            return Equals(otherProperty);
        }

        public static implicit operator T(PropertyBase<T> property) => property.Value;

        public static bool operator ==(PropertyBase<T> p1, PropertyBase<T> p2) => p1.Equals(p2);

        public static bool operator !=(PropertyBase<T> p1, PropertyBase<T> p2) => !(p1 == p2);

        public PropertyBase<T> IfWith(Action<T> action)
        {
            if (WithValue) action(m_Value);
            return this;
        }

        public override void Clear()
        {
            WithValue = false;
            m_Value = default;
        }

        public override void Zero() => Value = default;

        public T OrElse(T @default) => WithValue ? m_Value : @default;

        /// <returns>False if types are different. Equal if both values are the same, or if both do not have values.</returns>
        public sealed override bool Equals(PropertyBase other)
        {
            return other.GetType() == GetType()
                && WithValue && other.WithValue && ValueEquals((PropertyBase<T>) other)
                || WithoutValue && other.WithoutValue;
        }

        /// <summary>Use on two properties that are known to have values.</summary>
        /// <returns>False if types are different. Equal if both values are the same.</returns>
        /// <exception cref="WithoutValueException">Without value on at least one property.</exception>
        public abstract bool ValueEquals(PropertyBase<T> other);

        public override string ToString() => WithValue ? m_Value.ToString() : "No Value";

        /// <exception cref="ArgumentException">If types are different.</exception>
        public override void SetFromIfWith(PropertyBase other)
        {
            if (!(other is PropertyBase<T> otherProperty))
                throw new ArgumentException("Other property is not of the same type");
            if (otherProperty.WithValue)
                Value = otherProperty.m_Value;
        }

        /// <exception cref="ArgumentException">If types are different.</exception>
        public sealed override void InterpolateFromIfWith(PropertyBase p1, PropertyBase p2, float interpolation)
        {
            if (!(p1 is PropertyBase<T>) || !(p2 is PropertyBase<T>))
                throw new ArgumentException("Properties are not the proper type!");
            if (Field != null)
            {
                if (Field.IsDefined(typeof(CustomInterpolationAttribute))) return;
                if (Field.IsDefined(typeof(TakeSecondForInterpolationAttribute)))
                {
                    SetFromIfWith(p2);
                    return;
                }
            }
            ValueInterpolateFrom((PropertyBase<T>) p1, (PropertyBase<T>) p2, interpolation);
        }

        /// <summary>Interpolates into this from two properties that are known to have values.</summary>
        /// <exception cref="WithoutValueException">If <see cref="p1"/> or <see cref="p2"/> is without a value.</exception>
        public virtual void ValueInterpolateFrom(PropertyBase<T> p1, PropertyBase<T> p2, float interpolation) => SetFromIfWith(p2);

        public sealed override void Serialize(BinaryWriter writer)
        {
            if (DontSerialize || Field != null && Field.IsDefined(typeof(NoSerialization))) return;
            writer.Write(WithValue);
            if (WithoutValue) return;
            SerializeValue(writer);
        }

        public sealed override void Deserialize(BinaryReader reader)
        {
            if (DontSerialize || Field != null && Field.IsDefined(typeof(NoSerialization))) return;
            WithValue = reader.ReadBoolean();
            if (WithoutValue) return;
            DeserializeValue(reader);
        }

        /// <exception cref="WithoutValueException">If without value.</exception>
        public abstract void SerializeValue(BinaryWriter writer);

        /// <exception cref="WithoutValueException">If without value.</exception>
        public abstract void DeserializeValue(BinaryReader reader);
    }
}