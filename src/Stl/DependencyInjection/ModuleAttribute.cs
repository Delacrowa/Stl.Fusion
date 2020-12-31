using System;
using Microsoft.Extensions.DependencyInjection;
using Stl.Reflection;
using Stl.Text;

namespace Stl.DependencyInjection
{
    [Serializable]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ModuleAttribute : ServiceAttributeBase
    {
        public static Symbol DefaultScope = "Module";

        public ModuleAttribute()
        {
            // Let's make sure Modules aren't auto-registered together
            // with regular services: most likely this isn't intentional.
            Scope = DefaultScope;
        }

        public override void Register(IServiceCollection services, Type implementationType)
        {
            var module = (IModule) implementationType.CreateInstance();
            module.Services = services;
            module.ConfigureServices();
        }
    }
}
