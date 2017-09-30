using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace SignService.Controllers
{
    [Authorize(Roles = "admin_signservice")]
    [Authorize]
    [RequireHttps]
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}