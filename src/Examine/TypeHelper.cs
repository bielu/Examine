using System;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Compilation;
using System.Web.Hosting;

namespace Examine
{
    //TODO: We need to change this name, or make it internal again it conflicts with stuff in umb
    public static class TypeHelper
    {
        /// <summary>
        /// Find a type by name
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        /// <remarks>
        /// Takes into account if this is an aspnet hosted application
        /// </remarks>
        public static Type FindType(string typeName)
        {
            var isHosted = HttpContext.Current != null || HostingEnvironment.IsHosted;

            return isHosted 
                ? BuildManager.GetType(typeName, false) 
                : Type.GetType(typeName); //TODO: This won't always work, you need to load the assembly and then get the type
        }
    }
}
