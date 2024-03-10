﻿using Bookify.Web.Core.Consts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Encodings.Web;
using WhatsAppCloudApi;
using WhatsAppCloudApi.Services;

namespace Bookify.Web.Controllers
{
    [Authorize(Roles = AppRoles.Reception)]
    public class SubscribersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IDataProtector _dataProtector;
        private readonly IMapper _mapper;
        private readonly IWhatsAppClient _whatsAppClient;

        private readonly IImageService _imageService;
        private readonly IEmailBodyBuilder _emailBodyBuilder;
        private readonly IEmailSender _emailSender;

        public SubscribersController(ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IDataProtectionProvider dataProtector,
            IMapper mapper,
            IWhatsAppClient whatsAppClient,
            IImageService imageService,
            IEmailBodyBuilder emailBodyBuilder,
            IEmailSender emailSender)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _dataProtector = dataProtector.CreateProtector("MySecureKey");
            _mapper = mapper;
            _whatsAppClient = whatsAppClient;
            _imageService = imageService;
            _emailBodyBuilder = emailBodyBuilder;
            _emailSender = emailSender;
        }


        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Search(SearchFormViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var subscriber = _context.Subscribers
                            .SingleOrDefault(s => 
                                    s.Email == model.Value
                                || s.NationalId == model.Value
                                || s.MobileNumber == model.Value);

            var viewModel = _mapper.Map<SubscriberSearchResultViewModel>(subscriber);

            if(subscriber is not null)
                viewModel.Key = _dataProtector.Protect(subscriber.Id.ToString());

            return PartialView("_Result", viewModel);
        }

        public IActionResult Details(string id)
        {
            var subscriberId = int.Parse(_dataProtector.Unprotect(id));

            var subscriber = _context.Subscribers
                .Include(s => s.Governorate)
                .Include(s => s.Area)
                .SingleOrDefault(s => s.Id == subscriberId);

            if (subscriber is null)
                return NotFound();

            var viewModel = _mapper.Map<SubscriberViewModel>(subscriber);
            viewModel.Key = id;

            return View(viewModel);
        }

        public IActionResult Create()
        {
            var viewModel = PopulateViewModel();
            return View("Form", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SubscriberFormViewModel model)
        {
            if (!ModelState.IsValid)
                return View("Form", PopulateViewModel(model));

            var subscriber = _mapper.Map<Subscriber>(model);

            var imageName = $"{Guid.NewGuid()}{Path.GetExtension(model.Image!.FileName)}";
            var imagePath = "/images/Subscribers";

            var (isUploaded, errorMessage) = await _imageService.UploadAsync(model.Image, imageName, imagePath, hasThumbnail: true);

            if (!isUploaded)
            {
                ModelState.AddModelError("Image", errorMessage!);
                return View("Form", PopulateViewModel(model));
            }

			subscriber.ImageUrl = $"{imagePath}/{imageName}";
			subscriber.ImageThumbnailUrl = $"{imagePath}/thumb/{imageName}";
			subscriber.CreatedById = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            _context.Add(subscriber);
            _context.SaveChanges();

            //Send welcome email
            var placeholders = new Dictionary<string, string>()
            {
                { "imageUrl", "https://res.cloudinary.com/devcreed/image/upload/v1668739431/icon-positive-vote-2_jcxdww.svg" },
                { "header", $"Welcome {model.FirstName}," },
                { "body", "thanks for joining Bookify 🤩" }
            };

            var body = _emailBodyBuilder.GetEmailBody(EmailTemplates.Notification, placeholders);

            await _emailSender.SendEmailAsync(
                model.Email,
                "Welcome to Bookify", body);

            //Send welcome message using WhatsApp
            if (model.HasWhatsApp)
            {
                var components = new List<WhatsAppComponent>()
                {
                    new WhatsAppComponent
                    {
                        Type = "body",
                        Parameters = new List<object>()
                        {
                            new WhatsAppTextParameter { Text = model.FirstName }
                        }
                    }
                };

                var mobileNumber = _webHostEnvironment.IsDevelopment() ? "Add You Number" : model.MobileNumber;

                //Change 2 with your country code
                await _whatsAppClient
                    .SendMessage($"2{mobileNumber}", WhatsAppLanguageCode.English_US,
                    WhatsAppTemplates.WelcomeMessage, components);
            }

            var subscriberId = _dataProtector.Protect(subscriber.Id.ToString());

            return RedirectToAction(nameof(Details), new { id = subscriberId });
        }

        public IActionResult Edit(string id)
        {
            var subscriberId = int.Parse(_dataProtector.Unprotect(id));
            var subscriber = _context.Subscribers.Find(subscriberId);

            if (subscriber is null)
                return NotFound();

            var model = _mapper.Map<SubscriberFormViewModel>(subscriber);
            var viewModel = PopulateViewModel(model);
            viewModel.Key = id;

            return View("Form", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(SubscriberFormViewModel model)
        {
            if (!ModelState.IsValid)
                return View("Form", PopulateViewModel(model));

            var subscriberId = int.Parse(_dataProtector.Unprotect(model.Key));

            var subscriber = _context.Subscribers.Find(subscriberId);

            if (subscriber is null)
                return NotFound();

            if (model.Image is not null)
            {
                if (!string.IsNullOrEmpty(subscriber.ImageUrl))
                    _imageService.Delete(subscriber.ImageUrl, subscriber.ImageThumbnailUrl);

                var imageName = $"{Guid.NewGuid()}{Path.GetExtension(model.Image.FileName)}";
                var imagePath = "/images/Subscribers";

                var (isUploaded, errorMessage) = await _imageService.UploadAsync(model.Image, imageName, imagePath, hasThumbnail: true);

                if (!isUploaded)
                {
                    ModelState.AddModelError("Image", errorMessage!);
                    return View("Form", PopulateViewModel(model));
                }

                model.ImageUrl = $"{imagePath}/{imageName}";
                model.ImageThumbnailUrl = $"{imagePath}/thumb/{imageName}";
            }

            else if (!string.IsNullOrEmpty(subscriber.ImageUrl))
            {
                model.ImageUrl = subscriber.ImageUrl;
                model.ImageThumbnailUrl = subscriber.ImageThumbnailUrl;
            }

            subscriber = _mapper.Map(model, subscriber);
            subscriber.LastUpdatedById = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
            subscriber.LastUpdatedOn = DateTime.Now;

            _context.SaveChanges();

            return RedirectToAction(nameof(Details), new { id = model.Key });
        }

        [AjaxOnly]
        public IActionResult GetAreas(int governorateId)
        {
            var areas = _context.Areas
                    .Where(a => a.GovernorateId == governorateId && !a.IsDeleted)
                    .OrderBy(g => g.Name)
                    .ToList();

            return Ok(_mapper.Map<IEnumerable<SelectListItem>>(areas));
        }

        public IActionResult AllowNationalId(SubscriberFormViewModel model)
        {
            var subscriberId = 0;

            if (!string.IsNullOrEmpty(model.Key))
                subscriberId = int.Parse(_dataProtector.Unprotect(model.Key));

            var subscriber = _context.Subscribers.SingleOrDefault(b => b.NationalId == model.NationalId);
            var isAllowed = subscriber is null || subscriber.Id.Equals(subscriberId);

            return Json(isAllowed);
        }

        public IActionResult AllowMobileNumber(SubscriberFormViewModel model)
        {
            var subscriberId = 0;

            if (!string.IsNullOrEmpty(model.Key))
                subscriberId = int.Parse(_dataProtector.Unprotect(model.Key));

            var subscriber = _context.Subscribers.SingleOrDefault(b => b.MobileNumber == model.MobileNumber);
            var isAllowed = subscriber is null || subscriber.Id.Equals(subscriberId);

            return Json(isAllowed);
        }

        public IActionResult AllowEmail(SubscriberFormViewModel model)
        {
            var subscriberId = 0;

            if (!string.IsNullOrEmpty(model.Key))
                subscriberId = int.Parse(_dataProtector.Unprotect(model.Key));

            var subscriber = _context.Subscribers.SingleOrDefault(b => b.Email == model.Email);
            var isAllowed = subscriber is null || subscriber.Id.Equals(subscriberId);

            return Json(isAllowed);
        }

        private SubscriberFormViewModel PopulateViewModel(SubscriberFormViewModel? model = null)
        {
            SubscriberFormViewModel viewModel = model is null ? new SubscriberFormViewModel() : model;

            var governorates = _context.Governorates.Where(a => !a.IsDeleted).OrderBy(a => a.Name).ToList();
            viewModel.Governorates = _mapper.Map<IEnumerable<SelectListItem>>(governorates);
            
            if(model?.GovernorateId > 0)
            {
                var areas = _context.Areas
                    .Where(a => a.GovernorateId == model.GovernorateId && !a.IsDeleted)
                    .OrderBy(a => a.Name)
                    .ToList();

                viewModel.Areas = _mapper.Map<IEnumerable<SelectListItem>>(areas);
            }
            
            return viewModel;
        }
    }
}