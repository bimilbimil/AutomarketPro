using System;
using System.Threading.Tasks;

namespace AutomarketPro.Automation
{
    public abstract class AutomationBase
    {
        protected readonly AutomarketProPlugin Plugin;

        public Action<string>? Log { get; set; }
        public Action<string, Exception?>? LogError { get; set; }

        protected AutomationBase(AutomarketProPlugin plugin)
        {
            Plugin = plugin;
        }

        protected async Task<bool> RunOnFrameworkThreadAsync(Func<bool> action)
        {
            try
            {
                bool result = false;
                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (System.AccessViolationException ex)
                    {
                        LogError?.Invoke("Access violation in UI operation", ex);
                        result = false;
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke("Error in UI operation", ex);
                        result = false;
                    }
                });
                return result;
            }
            catch (Exception ex)
            {
                LogError?.Invoke("Error running on framework thread", ex);
                return false;
            }
        }

        protected async Task RunOnFrameworkThreadAsync(Action action)
        {
            try
            {
                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (System.AccessViolationException ex)
                    {
                        LogError?.Invoke("Access violation in UI operation", ex);
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke("Error in UI operation", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                LogError?.Invoke("Error running on framework thread", ex);
            }
        }
    }
}
