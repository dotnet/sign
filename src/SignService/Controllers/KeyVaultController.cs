using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignService.Models;
using SignService.Services;
using SignService.Utils;

namespace SignService.Controllers
{
    [Authorize(Roles = "admin_signservice")]
    [RequireHttps]
    public class KeyVaultController : Controller
    {
        readonly IKeyVaultAdminService keyVaultAdminService;

        public KeyVaultController(IKeyVaultAdminService keyVaultAdminService)
        {
            this.keyVaultAdminService = keyVaultAdminService;
        }
        // GET: KeyVault
        public async Task<IActionResult> Index()
        {
            var vaults = await keyVaultAdminService.ListKeyVaultsAsync();

            return View(vaults);
        }

        // GET: KeyVault/Details/5
        public async Task<IActionResult> Details(string id)
        {
            try
            {
                var vault = await keyVaultAdminService.GetVaultAsync(id);
                var certificates = await keyVaultAdminService.GetCertificatesInVaultAsync(vault.VaultUri);

                // See if there are any pending ops
                var ops = certificates.Select(c => new { cert = c, operation = keyVaultAdminService.GetCertificateOperation(vault.VaultUri, c.Name) }).ToList();
                await Task.WhenAll(ops.Select(a => a.operation));

                foreach (var op in ops)
                {
                    op.cert.Operation = op.operation.Result; // completed, safe
                }

                var model = new KeyVaultDetailsModel
                {
                    Vault = vault,
                    CertificateModels = ops.Select(a => a.cert).ToList()
                };

                return View(model);
            }
            catch (Exception)
            {
                ViewBag.Id = id;
                return View("Details.Error");
            }
        }

        // GET: KeyVault/CreateCertificate/vaultName
        public IActionResult CreateCertificate(string id)
        {
            return View(new CreateCertificateRequestModel { VaultName = id });
        }

        // POST: KeyVault/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCertificate(CreateCertificateRequestModel model)
        {
            try
            {
                var csr = await keyVaultAdminService.CreateCsrAsync(model.VaultName, model.CertificateName, model.CertificateName);

                return RedirectToAction(nameof(Details), new { id = model.VaultName });
            }
            catch (Exception e)
            {
                ModelState.AddModelError("", e.Message);
                return View(model);
            }
        }

        public async Task<IActionResult> CertificateRequest(string id, string certificateName)
        {
            var vault = await keyVaultAdminService.GetVaultAsync(id);
            var op = await keyVaultAdminService.GetCertificateOperation(vault.VaultUri, certificateName);

            string str = null;
            if (op.Csr?.Length > 0)
            {
                str = Crypt32.CryptBinaryToString(op.Csr, true, true);
            }
            var model = new UpdateCertificateRequestModel
            {
                CertificateName = certificateName,
                VaultName = id,
                Csr = str
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MergeCertificate(UpdateCertificateRequestModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(CertificateRequest), model);
            }

            byte[] data;
            using (var ms = new MemoryStream())
            {
                // get file data
                await model.Certificate.CopyToAsync(ms);
                ms.Position = 0;
                data = ms.ToArray();
            }

            try
            {
                await keyVaultAdminService.MergeCertificate(model.VaultName, model.CertificateName, data);
            }
            catch (Exception e)
            {
                ModelState.AddModelError("", e.Message);
                return View(nameof(CertificateRequest), model);
            }

            return RedirectToAction(nameof(Details), new { id = model.VaultName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelCertificateRequest(UpdateCertificateRequestModel model)
        {
            var op = await keyVaultAdminService.CancelCsrAsync(model.VaultName, model.CertificateName);

            return RedirectToAction(nameof(Details), new { id = model.VaultName });
        }
    }
}
