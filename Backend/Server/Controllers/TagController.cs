﻿using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Operators;

namespace Server.Controllers
{
    [ApiController]
    [Route("tags")]
    public class TagController : Controller
    {
        private readonly HttpContextAccessor _httpContextAccessor;
        private readonly AppDbContext _db;
        private readonly IAmazonS3 _s3Client;

        public TagController(AppDbContext db,
            IAmazonS3 s3Client)
        {
            _httpContextAccessor = new();
            _db = db;
            _s3Client = s3Client;
        }

        [Route("createtag")]
        [HttpPost]
        public async Task<ActionResult> CreateTag([FromBody] TagModel model)
        {
            var tagExists = _db.Tags.FirstOrDefault(tag => tag.Name == model.Name);
            if (tagExists != null)
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Tag {model.Name} already exists" });

            model.S3bucket = $"tag{Guid.NewGuid().ToString()}";
            if (!await BucketOperator.CreateBucketAsync(model.S3bucket, _s3Client))
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = "Failed creating S3 bucket" });

            _db.Tags.Add(model);
            _db.SaveChanges();

            return StatusCode(StatusCodes.Status201Created, new { Status = "Success" });
        }

        [Route("gettag/{id}")]
        [HttpGet]
        public async Task<ActionResult> GetTag(int id)
        {
            var tagExists = _db.Tags.FirstOrDefault(tag => tag.Id == id);
            if (tagExists == null)
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Tag with Id = {id} does not exist" });
            if (tagExists.S3bucket == null)
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Tag doesn't have bucket" });
            try
            {
                var s3Objects = await BucketOperator.GetObjectsFromBucket(tagExists.S3bucket, "tagpicture", _s3Client);
                return StatusCode(StatusCodes.Status200OK, new
                {
                    Status = "Success",
                    Tag = new
                    {
                        Id = tagExists.Id,
                        Name = tagExists.Name,
                        Picture = s3Objects
                    }
                });
            }
            catch
            {
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Can't load picture" });
            }            
        }

        [Route("updatetag/{id}")]
        [HttpPut]
        public ActionResult UpdateTag([FromBody] TagModel model, int id)
        {
            var tagExists = _db.Tags.FirstOrDefault(tag => tag.Id == id);
            if (tagExists != null)
            {
                tagExists.Name = model.Name;

                _db.SaveChanges();
                return StatusCode(StatusCodes.Status200OK, new { Status = "Success" });
            }
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Tag with Id = {id} does not exist" });
        }


        [Route("deletetag/{id}")]
        [HttpDelete]
        public async Task<ActionResult> DeleteTag(int id)
        {
            var tagExists = _db.Tags.FirstOrDefault(tag => tag.Id == id);
            if (tagExists != null)
            {
                var bucketExists = await _s3Client.DoesS3BucketExistAsync(tagExists.S3bucket);
                if (bucketExists)
                {
                    await _s3Client.DeleteObjectAsync(tagExists.S3bucket, "tagpicture");
                    await _s3Client.DeleteBucketAsync(tagExists.S3bucket);
                }

                _db.Tags.Remove(tagExists);
                _db.SaveChanges();
                return StatusCode(StatusCodes.Status200OK, new { Status = "Success" });
            }
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Tag with Id = {id} does not exist" });
        }

        [Route("getalltags")]
        [HttpGet]
        public async Task<ActionResult> GetAllTags()
        {
            var allTags = _db.Tags.ToArray();
            var resultTags = new List<object>();
            foreach (var tag in allTags)
            {
                IEnumerable<S3ObjectDtoModel>? s3Objects;
                if (tag.S3bucket == null)
                    s3Objects = null;
                else
                {
                    try
                    {
                        s3Objects = await BucketOperator.GetObjectsFromBucket(tag.S3bucket, "tagpicture", _s3Client);
                        
                    }
                    catch
                    {
                        return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Can't load picture in tag with id = {tag.Id}" });
                    }
                }
                resultTags.Add(new
                {
                    id = tag.Id,
                    name = tag.Name,
                    picture = s3Objects
                });
            }

            return StatusCode(StatusCodes.Status200OK, new { Status = "Success", Tags = resultTags });
        }

        [Route("updatetagpicture/{id}")]
        [HttpPut]
        public async Task<ActionResult> UpdateTagPicture([FromForm] IFormFile picture, int id)
        {
            var tagExists = _db.Tags.FirstOrDefault(tag => tag.Id == id);
            if (tagExists != null)
            {
                var bucketExists = await _s3Client.DoesS3BucketExistAsync(tagExists.S3bucket);
                if (!bucketExists)
                    return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = "S3 bucket does not exist" });

                var request = new PutObjectRequest()
                {
                    BucketName = tagExists.S3bucket,
                    Key = "tagpicture",
                    InputStream = picture.OpenReadStream()
                };
                request.Metadata.Add("Content-Type", picture.ContentType);
                await _s3Client.PutObjectAsync(request);

                _db.SaveChanges();
                return StatusCode(StatusCodes.Status200OK, new { Status = "Success", Message = "TagPicture updated successfully" });
            }
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Status = "Error", Message = $"Tag with Id = {id} does not exist" });
        }
    }
}
