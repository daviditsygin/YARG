using TMPro;
using UnityEngine;
using YARG.Core.Input;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Settings.Types;

namespace YARG.Gameplay.HUD
{
    public class DropdownPauseSetting : BasePauseSetting<IDropdownSetting>
    {
        [Space]
        [SerializeField]
        private TextMeshProUGUI _value;

        private string _settingName;

        public override void Initialize(string settingName, IDropdownSetting setting)
        {
            base.Initialize(settingName, setting);

            _settingName = settingName;
            RefreshVisual();
        }

        protected override NavigationScheme GetNavigationScheme()
        {
            return new NavigationScheme(new()
            {
                NavigateFinish,
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Next", () =>
                {
                    SelectIndex(Setting.CurrentIndex + 1);
                }),
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Previous", () =>
                {
                    SelectIndex(Setting.CurrentIndex - 1);
                })
            }, true);
        }

        public void OnValueClick()
        {
            SelectIndex(Setting.CurrentIndex + 1);
        }

        private void SelectIndex(int index)
        {
            if (index < 0)
            {
                index = Setting.Count - 1;
            }
            else if (index >= Setting.Count)
            {
                index = 0;
            }

            Setting.SelectIndex(index);
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            int index = Setting.CurrentIndex;
            if (index < 0)
            {
                _value.text = string.Empty;
                return;
            }

            string valueString = Setting.IndexToString(index);
            if (Setting.Localizable)
            {
                valueString = Localize.Key("Settings.Setting", _settingName, "Dropdown", valueString);
            }

            _value.text = valueString;
        }
    }
}
