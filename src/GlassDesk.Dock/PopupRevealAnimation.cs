using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassDesk.UI.Theming;

namespace GlassDesk.Dock;

internal sealed class PopupRevealAnimation
{
	private readonly Window _window;

	private readonly TranslateTransform _shift = new TranslateTransform();

	private readonly ThemeService? _theme;

	private long _generation;

	private static bool AnimationsEnabled
	{
		get
		{
			ThemeService? current = ThemeService.Current;
			if (current == null || current.AnimationsEnabled)
			{
				return SystemParameters.ClientAreaAnimation;
			}
			return false;
		}
	}

	internal PopupRevealAnimation(Window window, FrameworkElement content)
	{
		_window = window;
		content.RenderTransform = _shift;
		_theme = ThemeService.Current;
		if (_theme != null)
		{
			_theme.Changed += OnMotionPreferenceChanged;
		}
		SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
		window.IsVisibleChanged += OnVisibilityChanged;
		window.Closed += OnClosed;
	}

	internal void Prepare()
	{
		Finish();
		if (AnimationsEnabled)
		{
			_window.Opacity = 0.0;
			_shift.Y = -8.0;
		}
	}

	private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (!_window.IsVisible || !AnimationsEnabled)
		{
			Finish();
			return;
		}
		long generation = ++_generation;
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(200L);
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, 1.0, timeSpan)
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			if (generation == _generation)
			{
				Finish();
			}
		};
		_window.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		_shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8.0, 0.0, timeSpan)
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private void Finish()
	{
		_generation++;
		_window.Opacity = 1.0;
		_shift.Y = 0.0;
		_window.BeginAnimation(UIElement.OpacityProperty, null);
		_shift.BeginAnimation(TranslateTransform.YProperty, null);
	}

	private void OnMotionPreferenceChanged(object? sender, EventArgs e)
	{
		if (!AnimationsEnabled)
		{
			Finish();
		}
	}

	private void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "ClientAreaAnimation" && !AnimationsEnabled)
		{
			Finish();
		}
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		Finish();
		if (_theme != null)
		{
			_theme.Changed -= OnMotionPreferenceChanged;
		}
		SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
		_window.IsVisibleChanged -= OnVisibilityChanged;
		_window.Closed -= OnClosed;
	}
}
