using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinLoop.Models;

namespace WinLoop.Menus
{
    public abstract class RadialMenu : Canvas
    {
        public event Action<MenuItemPosition> OnItemSelected;
        
        protected Point CenterPoint { get; private set; }
        protected AppConfig Config { get; private set; }
        
        public void Initialize(AppConfig config, Point centerPoint)
        {
            Config = config;
            CenterPoint = centerPoint;
            InitializeMenu();
        }
        
        protected abstract void InitializeMenu();
        
        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point mousePos = e.GetPosition(this);
            MenuItemPosition? selectedItem = GetSelectedItem(mousePos);
            if (selectedItem.HasValue)
            {
                HighlightItem(selectedItem.Value);
            }
        }
        
        protected override void OnMouseUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
            {
                Point mousePos = e.GetPosition(this);
                MenuItemPosition? selectedItem = GetSelectedItem(mousePos);
                if (selectedItem.HasValue)
                {
                    OnItemSelected?.Invoke(selectedItem.Value);
                }
            }
        }
        
        public abstract MenuItemPosition? GetSelectedItem(Point mousePosition);
        public abstract void HighlightItem(MenuItemPosition itemPosition);
        public abstract void ClearHighlight();
    }
}