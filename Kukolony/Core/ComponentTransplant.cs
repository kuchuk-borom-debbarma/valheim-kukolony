using System;
using System.Reflection;
using UnityEngine;

namespace Kukolony.Core
{
    /// <summary>
    ///     Moves authored field values from one component to another.
    ///
    ///     Needed because our villager is a clone of the Player prefab with the Player
    ///     component swapped for a plain Humanoid. Player derives from Humanoid derives
    ///     from Character, so every value the prefab author set - health, speed, effect
    ///     lists, animator and collider references - already exists on the Player
    ///     component. Reflection is the only way to carry them across, since they are
    ///     serialized fields rather than anything with a public setter.
    /// </summary>
    internal static class ComponentTransplant
    {
        private const BindingFlags DeclaredInstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        ///     Copies every instance field declared directly on <paramref name="declaringType" />
        ///     from one object to another.
        /// </summary>
        /// <param name="declaringType">
        ///     Call once per level of the hierarchy - DeclaredOnly means base-class fields
        ///     are not included, which keeps the copy explicit about what it touches.
        /// </param>
        internal static int CopyDeclaredFields(Type declaringType, object source, object target)
        {
            int copied = 0;

            foreach (FieldInfo field in declaringType.GetFields(DeclaredInstanceFields))
            {
                // readonly fields are initialised by the target's own field initialisers.
                // Overwriting them would share a reference the target expects to own -
                // Humanoid.m_inventory being the one that matters.
                if (field.IsInitOnly)
                {
                    continue;
                }

                field.SetValue(target, field.GetValue(source));
                copied++;
            }

            return copied;
        }

        /// <summary>
        ///     Removes a component if present. Uses DestroyImmediate because this runs on
        ///     a prefab during registration, where deferred destruction would leave the
        ///     component alive for the rest of the frame and get cloned into instances.
        /// </summary>
        internal static void RemoveIfPresent<T>(GameObject prefab) where T : Component
        {
            if (prefab.TryGetComponent(out T component))
            {
                UnityEngine.Object.DestroyImmediate(component, allowDestroyingAssets: true);
            }
        }
    }
}
