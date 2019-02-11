﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Edi.Blog.Pingback;
using Edi.Blog.Pingback.MvcExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Core;
using Moonglade.Data;
using Moonglade.Data.Entities;
using Moonglade.Model;
using Moonglade.Model.Settings;
using Moonglade.Web.Filters;
using Moonglade.Web.Models;
using X.PagedList;

namespace Moonglade.Web.Controllers
{
    [Route("post")]
    public partial class PostController : MoongladeController
    {
        private readonly PostService _postService;

        private readonly CategoryService _categoryService;

        private readonly PingbackSender _pingbackSender;

        public PostController(MoongladeDbContext context,
            ILogger<PostController> logger,
            IOptions<AppSettings> settings,
            IConfiguration configuration,
            IHttpContextAccessor accessor, PostService postService, CategoryService categoryService, PingbackSender pingbackSender)
            : base(context, logger, settings, configuration, accessor)
        {
            _postService = postService;
            _categoryService = categoryService;
            _pingbackSender = pingbackSender;
        }

        [Route(""), Route("/")]
        public IActionResult Index(int page = 1)
        {
            var postsAsIPagedList = GetPagedPostsViewList((x, y, z) => _postService.GetPagedPost(x, page), page);
            return View(postsAsIPagedList);
        }

        [Route("{year:int:min(2008):max(2108):length(4)}/{month:int:range(1,12)}/{day:int:range(1,31)}/{slug}")]
        [AddPingbackHeader("pingback")]
        public IActionResult Slug(int year, int month, int day, string slug)
        {
            ViewBag.ErrorMessage = string.Empty;

            if (year > DateTime.UtcNow.Year || string.IsNullOrWhiteSpace(slug))
            {
                Logger.LogWarning($"Invalid parameter year: {year}, slug: {slug}");
                return NotFound();
            }

            var post = _postService.GetSingleSlug(year, month, day, slug);
            if (post == null)
            {
                Logger.LogWarning($"Post not found for parameter {year}/{month}/{day}/{slug}.");
                return NotFound();
            }

            var viewModel = new PostSlugViewModelWrapper();

            #region Fetch Post Main Model

            var postModel = new PostSlugViewModel
            {
                Title = post.Title,
                Abstract = post.ContentAbstract,
                PubDateUtc = post.PostPublish.PubDateUtc.GetValueOrDefault(),

                Categories = post.PostCategory.Select(pc => pc.Category).Select(p => new PostDetailViewCategoryInfo
                {
                    CategoryDisplayName = p.DisplayName,
                    CategoryRouteName = p.Title
                }).ToList(),

                Content = HttpUtility.HtmlDecode(post.PostContent),
                Hits = post.PostExtension.Hits,
                LikeHits = post.PostExtension.Likes.GetValueOrDefault(),

                Tags = post.PostTag.Select(pt => pt.Tag)
                                   .Select(p => new TagInfo
                                   {
                                       NormalizedTagName = p.NormalizedName,
                                       TagName = p.DisplayName
                                   }).ToList(),
                PostId = post.Id.ToString(),
                CommentEnabled = post.CommentEnabled ?? false,
                IsExposedToSiteMap = post.PostPublish.ExposedToSiteMap,
                LastModifyOn = post.PostPublish.LastModifiedUtc,
                CommentCount = post.Comment.Count(c => null != c.IsApproved && c.IsApproved.Value)
            };

            if (AppSettings.EnableImageLazyLoad)
            {
                var rawHtmlContent = postModel.Content;
                postModel.Content = Utils.ReplaceImgSrc(rawHtmlContent);
            }

            viewModel.PostModel = postModel;
            viewModel.CommentPostModel.PostId = post.Id;

            #endregion Fetch Post Main Model

            ViewBag.TitlePrefix = $"{post.Title}";

            return View(viewModel);
        }

        [HttpPost("hit")]
        [ValidateAntiForgeryToken]
        public IActionResult Hit([FromForm] Guid postId)
        {
            if (HasCookie(CookieNames.Hit, postId.ToString()))
            {
                Logger.LogTrace($"User from {HttpContext.Connection.RemoteIpAddress} re-visited post {postId}");
                return new EmptyResult();
            }

            var response = _postService.UpdatePostHit(postId);
            if (response.IsSuccess)
            {
                SetPostTrackingCookie(CookieNames.Hit, postId.ToString());
                Logger.LogTrace($"User from {HttpContext.Connection.RemoteIpAddress} hit post: {postId}");
            }

            return Json(response);
        }

        [HttpPost("like")]
        [ValidateAntiForgeryToken]
        public IActionResult Like([FromForm] Guid postId)
        {
            if (HasCookie(CookieNames.Liked, postId.ToString()))
            {
                return Json(new
                {
                    IsSuccess = false,
                    Message = "You Have Rated"
                });
            }

            var response = _postService.Like(postId);
            if (response.IsSuccess)
            {
                SetPostTrackingCookie(CookieNames.Liked, postId.ToString());
                Logger.LogInformation($"User from {HttpContext.Connection.RemoteIpAddress} liked post: {postId}");
            }

            return Json(response);
        }

        #region Helper Methods

        private StaticPagedList<Post> GetPagedPostsViewList(
        Func<int, int, string, IEnumerable<Post>> postListFunc,
        int page, int? pageSize = null, string author = null)
        {
            int pagesize = pageSize ?? AppSettings.PostListPageSize;
            var postList = postListFunc(pagesize, page, author);
            var postsAsIPagedList = new StaticPagedList<Post>(postList, page, pagesize, _postService.CountForPublished);
            return postsAsIPagedList;
        }

        private bool HasCookie(CookieNames cookieName, string id)
        {
            var viewCookie = HttpContextAccessor.HttpContext.Request.Cookies[cookieName.ToString()];
            if (viewCookie != null)
            {
                return viewCookie == id;
            }
            return false;
        }

        private void SetPostTrackingCookie(CookieNames cookieName, string id)
        {
            var options = new CookieOptions
            {
                Expires = DateTime.UtcNow.AddDays(1),
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,

                // Mark as essential to pass GDPR
                // https://docs.microsoft.com/en-us/aspnet/core/security/gdpr?view=aspnetcore-2.1
                IsEssential = true
            };

            Response.Cookies.Append(cookieName.ToString(), id, options);
        }

        #endregion
    }
}