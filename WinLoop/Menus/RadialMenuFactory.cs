using WinLoop.Models;

namespace WinLoop.Menus
{
    public class RadialMenuFactory
    {
        public RadialMenu CreateMenu(MenuStyle menuStyle, AppConfig config)
        {
            switch (menuStyle)
            {
                case MenuStyle.CSHeadshotOctagon:
                    return new CSHeadshotMenu();
                case MenuStyle.SpiderWeb:
                    return new SpiderWebMenu();
                case MenuStyle.Bagua:
                    return new BaguaMenu();
                case MenuStyle.BasicRadial:
                default:
                    return new BasicRadialMenu();
            }
        }
    }
}