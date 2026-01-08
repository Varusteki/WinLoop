using WinLoop.Models;

namespace WinLoop.Menus
{
    public class RadialMenuFactory
    {
        public RadialMenu CreateMenu(MenuStyle menuStyle, AppConfig config)
        {
            switch (menuStyle)
            {
                case MenuStyle.BasicRadial:
                    return new BasicRadialMenu();
                // Other menu styles are disabled; always fall back to the ring menu.
                default:
                    return new BasicRadialMenu();
            }
        }
    }
}