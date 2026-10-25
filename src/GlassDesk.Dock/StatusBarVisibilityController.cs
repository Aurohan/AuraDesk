using System;
using GlassDesk.Core.Models.Settings;

namespace GlassDesk.Dock;

public sealed class StatusBarVisibilityController
{
	private AutoHideController? _autoHide;

	private int _delay;

	private bool _revealedDuringSuppression;

	private bool _pointerWasOver;

	public AutoHideState Update(DockSettings settings, DateTime now, bool pointerOver, bool covered, bool fullscreen, bool launcherActive, bool revealLocked, bool enabled, bool sleeping)
	{
		bool flag = pointerOver && !_pointerWasOver;
		_pointerWasOver = pointerOver;
		if (_autoHide == null || _delay != settings.AutoHideLeaveDelayMs)
		{
			_delay = settings.AutoHideLeaveDelayMs;
			_autoHide = new AutoHideController(Math.Clamp(_delay, 0, 3000));
		}
		if (!enabled | sleeping)
		{
			_revealedDuringSuppression = false;
			_autoHide.ForceHide(now);
		}
		else if (launcherActive)
		{
			_revealedDuringSuppression = false;
			_autoHide.ForceShown(now);
		}
		else if (covered | fullscreen)
		{
			if (revealLocked)
			{
				_autoHide.ForceHide(now);
			}
			else if (_autoHide.State == AutoHideState.Hidden)
			{
				if (flag && _autoHide.TryReveal(isCovered: false, now))
				{
					_revealedDuringSuppression = true;
				}
			}
			else if (_revealedDuringSuppression)
			{
				PollPointer(now, pointerOver);
			}
			else
			{
				_autoHide.ForceHide(now);
			}
		}
		else
		{
			if (!pointerOver)
			{
				_revealedDuringSuppression = false;
			}
			if (settings.AutoHide)
			{
				PollPointer(now, pointerOver);
			}
			else
			{
				_autoHide.ForceShown(now);
			}
		}
		if (_autoHide.State == AutoHideState.Hidden)
		{
			_revealedDuringSuppression = false;
		}
		return _autoHide.State;
	}

	private void PollPointer(DateTime now, bool pointerOver)
	{
		if (pointerOver)
		{
			_autoHide.PointerEnter(now);
		}
		else
		{
			_autoHide.PointerLeave(now);
		}
		_autoHide.Poll(now);
	}
}
