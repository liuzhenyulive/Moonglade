﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Edi.Blog.Pingback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moonglade.Core;
using Moonglade.Model;
using Moonglade.Web.Filters;
using Moonglade.Web.Models;

namespace Moonglade.Web.Controllers
{
    public partial class PostController
    {
        #region Management

        [Authorize]
        [Route("manage")]
        public async Task<IActionResult> Manage()
        {
            var list = await _postService.GetPostMetaListAsync();
            return View(list);
        }

        [Authorize]
        [Route("manage/draft")]
        public async Task<IActionResult> Draft()
        {
            var list = await _postService.GetPostMetaListAsync(false, false);
            return View(list);
        }

        [Authorize]
        [Route("manage/recycle-bin")]
        public async Task<IActionResult> RecycleBin()
        {
            var list = await _postService.GetPostMetaListAsync(true);
            return View(list);
        }

        [Authorize]
        [Route("manage/create")]
        public async Task<IActionResult> Create()
        {
            var view = await GetCreatePostModelAsync();
            return View("CreateOrEdit", view);
        }

        [Authorize]
        [HttpPost("manage/create")]
        [ServiceFilter(typeof(DeleteSubscriptionCache))]
        [TypeFilter(typeof(DeleteMemoryCache), Arguments = new object[] { StaticCacheKeys.PostCount })]
        public IActionResult Create(PostEditViewModel model, 
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] IPingbackSender pingbackSender)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    // get tags
                    string[] tagList = string.IsNullOrWhiteSpace(model.Tags)
                                             ? new string[] { }
                                             : model.Tags.Split(',').ToArray();

                    var request = new CreatePostRequest
                    {
                        Title = model.Title.Trim(),
                        Slug = model.Slug.Trim(),
                        HtmlContent = model.HtmlContent,
                        EnableComment = model.EnableComment,
                        ExposedToSiteMap = model.ExposedToSiteMap,
                        IsFeedIncluded = model.FeedIncluded,
                        ContentLanguageCode = model.ContentLanguageCode,
                        IsPublished = model.IsPublished,
                        Tags = tagList,
                        CategoryIds = model.SelectedCategoryIds
                    };

                    var response = _postService.CreateNewPost(request);
                    if (response.IsSuccess)
                    {
                        if (!model.IsPublished) return RedirectToAction(nameof(Manage));

                        var pubDate = response.Item.PostPublish.PubDateUtc.GetValueOrDefault();
                        var link = GetPostUrl(linkGenerator, pubDate, response.Item.Slug);

                        if (AppSettings.EnablePingBackSend)
                        {
                            Task.Run(async () => { await pingbackSender.TrySendPingAsync(link, response.Item.PostContent); });
                        }

                        return RedirectToAction(nameof(Manage));
                    }

                    ModelState.AddModelError("", response.Message);
                    return View("CreateOrEdit", model);
                }

                return View("CreateOrEdit", model);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error Creating New Post.");
                ModelState.AddModelError("", ex.Message);
                return View("CreateOrEdit", model);
            }
        }


        [Authorize]
        [Route("manage/edit")]
        public async Task<IActionResult> Edit(Guid id, [FromServices] IHtmlCodec htmlCodec)
        {
            var postResponse = _postService.GetPost(id);
            if (!postResponse.IsSuccess)
            {
                return ServerError();
            }

            var post = postResponse.Item;
            if (null != post)
            {
                var editViewModel = new PostEditViewModel
                {
                    PostId = post.Id,
                    IsPublished = post.IsPublished,
                    HtmlContent = htmlCodec.HtmlDecode(post.EncodedHtmlContent),
                    Slug = post.Slug,
                    Title = post.Title,
                    EnableComment = post.CommentEnabled,
                    ExposedToSiteMap = post.ExposedToSiteMap,
                    FeedIncluded = post.FeedIncluded,
                    ContentLanguageCode = post.ContentLanguageCode
                };

                ViewBag.PubDateStr = $"{post.PubDateUtc.GetValueOrDefault():yyyy/M/d}";

                var tagStr = post.Tags
                                 .Select(p => p.TagName)
                                 .Aggregate(string.Empty, (current, item) => current + (item + ","));

                tagStr = tagStr.TrimEnd(',');
                editViewModel.Tags = tagStr;

                var catResponse = await _categoryService.GetAllCategoriesAsync();
                if (!catResponse.IsSuccess)
                {
                    return ServerError("Unsuccessful response from _categoryService.GetAllCategoriesAsync().");
                }

                var catList = catResponse.Item;
                if (null != catList && catList.Count > 0)
                {
                    var cbCatList = catList.Select(p =>
                        new CheckBoxViewModel(
                            p.DisplayName,
                            p.Id.ToString(),
                            post.Categories.Any(q => q.Id == p.Id))).ToList();
                    editViewModel.CategoryList = cbCatList;
                }

                return View("CreateOrEdit", editViewModel);
            }

            return NotFound();
        }

        [Authorize]
        [ServiceFilter(typeof(DeleteSubscriptionCache))]
        [TypeFilter(typeof(DeleteMemoryCache), Arguments = new object[] { StaticCacheKeys.PostCount })]
        [HttpPost("manage/edit")]
        public IActionResult Edit(PostEditViewModel model, 
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] IPingbackSender pingbackSender)
        {
            if (ModelState.IsValid)
            {
                string[] tagList = string.IsNullOrWhiteSpace(model.Tags)
                                         ? new string[] { }
                                         : model.Tags.Split(',').ToArray();

                var request = new EditPostRequest(model.PostId)
                {
                    Title = model.Title.Trim(),
                    Slug = model.Slug.Trim(),
                    HtmlContent = model.HtmlContent,
                    EnableComment = model.EnableComment,
                    ExposedToSiteMap = model.ExposedToSiteMap,
                    IsFeedIncluded = model.FeedIncluded,
                    ContentLanguageCode = model.ContentLanguageCode,
                    IsPublished = model.IsPublished,
                    Tags = tagList,
                    CategoryIds = model.SelectedCategoryIds
                };

                var response = _postService.EditPost(request);
                if (response.IsSuccess)
                {
                    if (model.IsPublished)
                    {
                        Logger.LogInformation($"Trying to Ping URL for post: {response.Item.Id}");

                        var pubDate = response.Item.PostPublish.PubDateUtc.GetValueOrDefault();
                        var link = GetPostUrl(linkGenerator, pubDate, response.Item.Slug);

                        if (AppSettings.EnablePingBackSend)
                        {
                            Task.Run(async () => { await pingbackSender.TrySendPingAsync(link, response.Item.PostContent); });
                        }
                    }

                    return RedirectToAction(nameof(Manage));
                }

                ModelState.AddModelError("", response.Message);
                return View("CreateOrEdit", model);

            }

            return View("CreateOrEdit", model);
        }

        [Authorize]
        [ServiceFilter(typeof(DeleteSubscriptionCache))]
        [TypeFilter(typeof(DeleteMemoryCache), Arguments = new object[] { StaticCacheKeys.PostCount })]
        [HttpPost("manage/restore")]
        public IActionResult Restore(Guid postId)
        {
            var response = _postService.RestoreDeletedPost(postId);
            return response.IsSuccess ? Json(postId) : ServerError();
        }

        [Authorize]
        [ServiceFilter(typeof(DeleteSubscriptionCache))]
        [TypeFilter(typeof(DeleteMemoryCache), Arguments = new object[] { StaticCacheKeys.PostCount })]
        [HttpPost("manage/delete")]
        public IActionResult Delete(Guid postId)
        {
            var response = _postService.Delete(postId, true);
            return response.IsSuccess ? Json(postId) : ServerError();
        }

        [Authorize]
        [ServiceFilter(typeof(DeleteSubscriptionCache))]
        [HttpPost("manage/delete-from-recycle")]
        public IActionResult DeleteFromRecycleBin(Guid postId)
        {
            var response = _postService.Delete(postId);
            if (response.IsSuccess)
            {
                return Json(postId);
            }

            return ServerError();
        }

        [Authorize]
        [ServiceFilter(typeof(DeleteSubscriptionCache))]
        [HttpGet("manage/empty-recycle-bin")]
        public async Task<IActionResult> EmptyRecycleBin()
        {
            await _postService.DeleteRecycledPostsAsync();
            return RedirectToAction("RecycleBin");
        }

        #endregion

        #region Helper Methods

        private async Task<PostEditViewModel> GetCreatePostModelAsync()
        {
            var view = new PostEditViewModel
            {
                PostId = Guid.NewGuid(),
                IsPublished = true,
                EnableComment = true,
                ExposedToSiteMap = true,
                FeedIncluded = true
            };

            var catList = await _categoryService.GetAllCategoriesAsync();
            if (null != catList.Item && catList.Item.Any())
            {
                var cbCatList = catList.Item.Select(p =>
                    new CheckBoxViewModel(p.DisplayName, p.Id.ToString(), false)).ToList();
                view.CategoryList = cbCatList;
            }

            return view;
        }

        #endregion
    }
}