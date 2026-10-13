using Unity.Properties;
using UnityEngine.UIElements;

namespace KERNEL.UI
{
    /// <summary>
    /// The converters KERNEL's UXML binds through, so its views show and hide elements from view model properties
    /// the way Redux's do (Redux's tracking station panel registers BoolToDisplayStyleConverter for its own). KERNEL
    /// registers its own rather than relying on Redux's, which are only registered once the map has been opened.
    /// </summary>
    internal static class KernelConverters
    {
        /// <summary>
        /// True shows the element, false leaves it out.
        /// </summary>
        public const string BoolToDisplay = "KernelBoolToDisplay";

        /// <summary>
        /// True leaves the element out, false shows it.
        /// </summary>
        public const string InvertedBoolToDisplay = "KernelInvertedBoolToDisplay";

        /// <summary>
        /// True turns an element a quarter turn, as an open row's arrow points down.
        /// </summary>
        public const string BoolToOpenArrow = "KernelBoolToOpenArrow";

        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            var display = new ConverterGroup(BoolToDisplay);
            display.AddConverter((ref bool value) => new StyleEnum<DisplayStyle>(value ? DisplayStyle.Flex : DisplayStyle.None));
            ConverterGroups.RegisterConverterGroup(display);

            var inverted = new ConverterGroup(InvertedBoolToDisplay);
            inverted.AddConverter((ref bool value) => new StyleEnum<DisplayStyle>(value ? DisplayStyle.None : DisplayStyle.Flex));
            ConverterGroups.RegisterConverterGroup(inverted);

            // Written out in full: with Unity.Properties in scope, a bare Rotate isn't the UI Toolkit one
            var arrow = new ConverterGroup(BoolToOpenArrow);
            arrow.AddConverter((ref bool value) => new StyleRotate(new UnityEngine.UIElements.Rotate(new UnityEngine.UIElements.Angle(value ? 90f : 0f))));
            ConverterGroups.RegisterConverterGroup(arrow);
        }
    }
}
