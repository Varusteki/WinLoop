using WinLoop.Menus;
using WinLoop.Models;

namespace WinLoop.Core
{
    public class RadialMenuFactory
    {
        public RadialMenu CreateMenu(MenuStyle menuStyle, AppConfig config)
        {
            switch (menuStyle)
            {
                case MenuStyle.BasicRadial:
                    return new BasicRadialMenu();
                case MenuStyle.CSHeadshotOctagon:
                    return new CSHeadshotMenu();
                case MenuStyle.SpiderWeb:
                    return new SpiderWebMenu();
                default:
                    return new BasicRadialMenu();
            }
        }
    }
}