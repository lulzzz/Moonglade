﻿using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Model.Settings;

namespace Moonglade.Web.Controllers
{
    public class MoongladeController : Controller
    {
        protected IConfiguration Configuration;

        protected readonly ILogger<ControllerBase> Logger;

        protected IMemoryCache Cache;

        protected AppSettings AppSettings { get; set; }

        public MoongladeController(
            ILogger<ControllerBase> logger = null,
            IOptions<AppSettings> settings = null,
            IConfiguration configuration = null,
            IMemoryCache memoryCache = null)
        {
            if (null != logger) Logger = logger;
            if (null != settings) AppSettings = settings.Value;
            if (null != configuration) Configuration = configuration;
            if (null != memoryCache) Cache = memoryCache;
        }

        [Route("server-error")]
        public IActionResult ServerError(string errMessage = "")
        {
            if (!string.IsNullOrWhiteSpace(errMessage))
            {
                Logger.LogError($"Server Error: {errMessage}");
            }
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        protected void SetFriendlyErrorMessage()
        {
            HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ViewBag.IsServerError = true;
        }

        protected string GetUserAgent()
        {
            return Request.Headers["User-Agent"];
        }

        protected string GetPostUrl(LinkGenerator linkGenerator, DateTime pubDate, string slug)
        {
            var link = linkGenerator.GetUriByAction(HttpContext, action: "Slug", controller: "Post",
                values: new
                {
                    year = pubDate.Year,
                    month = pubDate.Month,
                    day = pubDate.Day,
                    slug = slug
                });
            return link;
        }
    }
}