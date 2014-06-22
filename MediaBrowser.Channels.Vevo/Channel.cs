﻿using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Channels.Vevo
{
    public class Channel : IChannel, IRequiresMediaInfoCallback
    {
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;

        public Channel(IHttpClient httpClient, ILogManager logManager, IJsonSerializer jsonSerializer)
        {
            _httpClient = httpClient;
            _logger = logManager.GetLogger(GetType().Name);
            _jsonSerializer = jsonSerializer;
        }

        public string DataVersion
        {
            get
            {
                // Increment as needed to invalidate all caches
                return "5";
            }
        }

        public string Description
        {
            get { return "Watch music videos and original shows on Vevo. Download Vevo free on mobile and TV devices."; }
        }

        public bool IsEnabledFor(string userId)
        {
            return true;
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            _logger.Debug("Category ID " + query.FolderId);

            if (query.FolderId == null)
            {
                return await GetMainMenu(cancellationToken).ConfigureAwait(false);
            }

            var channel = query.FolderId.Split('_');

            if (channel[0] == "videos")
            {
                return await GetVideoArtistSubMenu("video", "nogenre", cancellationToken);
            }

            if (channel[0] == "genres")
            {
                return await AllGenresMenu(cancellationToken);
            }
            if (channel[0] == "genre")
            {
                return await GenresSubMenu(channel[1], cancellationToken);
            }

            if (channel[0] == "artists")
            {
                return await GetVideoArtistSubMenu("artistList", "nogenre", cancellationToken);
            }
            if (channel[0] == "artistList")
            {
                return await GetArtistListing(channel[1], channel[2], query, cancellationToken);
            }


            if (channel[0] == "artist" || channel[0] == "video")
            {
                return
                    await
                        GetVideoListing((channel[0] == "artist" ? "artist" : "video"), channel[1], channel[2], query,
                            cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private async Task<ChannelItemResult> GetMainMenu(CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>
            {
                new ChannelItemInfo
                {
                    Name = "Videos",
                    Id = "videos_",
                    Type = ChannelItemType.Folder
                },
                new ChannelItemInfo
                {
                    Name = "Artists",
                    Id = "artists_",
                    Type = ChannelItemType.Folder
                },
                new ChannelItemInfo
                {
                    Name = "Genres",
                    Id = "genres_",
                    Type = ChannelItemType.Folder
                }
            };

            return new ChannelItemResult
            {
                Items = items.ToList()
            };
        }

        private async Task<ChannelItemResult> AllGenresMenu(CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();
            Info.Genre info;

            using (var site = await _httpClient.Get("http://api.vevo.com/mobile/v1/genre/list.json", CancellationToken.None).ConfigureAwait(false))
            {
                info = _jsonSerializer.DeserializeFromStream<Info.Genre>(site);

                foreach (var r in info.result)
                {
                    var image = r.Key;

                    if (image == "latin-pop") image = "Latin";
                    else if (image == "rbsoul") image = "R&B";
                    else if (image == "electronicdance") image = "Electronic";
                    else if (image == "raphip-hop") image = "Rap & HipHop";
                    else if (image == "comedyhumor") image = "Humour";
                    else image = r.Value;

                    items.Add(new ChannelItemInfo
                    {
                        Name = r.Value,
                        Id = "genre_" + r.Key,
                        Type = ChannelItemType.Folder,
                        ImageUrl = "https://raw.githubusercontent.com/MediaBrowser/MediaBrowser.Resources/master/images/imagesbyname/musicgenres/" + image + "/thumb.jpg"
                    });
                }
            }
            
            return new ChannelItemResult
            {
                Items = items.ToList()
            };
        }

        private async Task<ChannelItemResult> GenresSubMenu(String genre, CancellationToken cancellationToken)
        {
            _logger.Debug("Genre : " + genre);
            var items = new List<ChannelItemInfo>
            {
                new ChannelItemInfo
                {
                    Name = "Videos",
                    Id = "video_norequest_" + genre,
                    Type = ChannelItemType.Folder
                },
                new ChannelItemInfo
                {
                    Name = "Artists",
                    Id = "video_MostViewedToday_" + genre,
                    Type = ChannelItemType.Folder 
                }
            };

            return new ChannelItemResult
            {
                Items = items.ToList()
            };
        }



        private async Task<ChannelItemResult> GetVideoArtistSubMenu(String type, String genre, CancellationToken cancellationToken)
        {
            if (genre == "") genre = "nogenre";

            var items = new List<ChannelItemInfo>
            {
                new ChannelItemInfo
                {
                    Name = "Most recent",
                    Id = type + "_MostRecent_" + genre,
                    Type = ChannelItemType.Folder
                },
                new ChannelItemInfo
                {
                    Name = "Most viewed today",
                    Id = type + "_MostViewedToday_" + genre,
                    Type = ChannelItemType.Folder 
                },
                new ChannelItemInfo
                {
                    Name = "Most viewed this week",
                    Id = type + "_MostViewedThisWeek_" + genre,
                    Type = ChannelItemType.Folder
                },
                new ChannelItemInfo
                {
                    Name = "Most viewed this month",
                    Id = type + "_MostViewedThisMonth_" + genre,
                    Type = ChannelItemType.Folder
                },
                new ChannelItemInfo
                {
                    Name = "Most viewed of all time",
                    Id = type + "_MostViewedAllTime_" + genre,
                    Type = ChannelItemType.Folder 
                }
            };

            return new ChannelItemResult
            {
                Items = items.ToList()
            };
        }


        private async Task<ChannelItemResult> GetArtistListing(String request, String genre, InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var offset = query.StartIndex.GetValueOrDefault();
            var items = new List<ChannelItemInfo>();
            var url = String.Format("http://api.vevo.com/mobile/v1/artist/list.json?max={0}&offset={1}&extended=true",
                query.Limit, offset);
            Info.ArtistList info;

            if (genre != "nogenre")
                url = url + "&genre=" + genre;
            if (request != "norequest")
                url = url + "&order=" + request;

            using (var site = await _httpClient.Get(url, CancellationToken.None).ConfigureAwait(false))
            {
                info = _jsonSerializer.DeserializeFromStream<Info.ArtistList>(site);

                foreach (var i in info.result)
                {
                    items.Add(new ChannelItemInfo
                    {
                        Name = i.name,
                        ImageUrl = i.image_url + "?width=512&height=512&crop=auto",
                        Id = "artist_" + i.url_safename + "_nogenre",
                        Type = ChannelItemType.Folder
                    });
                }
            }

            return new ChannelItemResult
            {
                Items = items.ToList(),
                TotalRecordCount = info.result.Count()
            };
        }


        private async Task<ChannelItemResult> GetVideoListing(String type, String request, String genre, InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var offset = query.StartIndex.GetValueOrDefault();
            var items = new List<ChannelItemInfo>();
            var url = "";
            Info.VideoList info;

            if (type == "video")
            {
                url = String.Format(
                    "http://api.vevo.com/mobile/v1/video/list.json?max={0}&offset={1}&extended=true",
                    query.Limit, offset);
               
                if (genre != "nogenre")
                    url = url + "&genre=" + genre;
                if (request != "norequest")
                    url = url + "&order=" + request;
            }
            else
            {
                url = String.Format(
                    "http://api.vevo.com/mobile/v1/artist/{0}/videos.json?max={1}&extended=true",
                    request, query.Limit);
            }

            using (var site = await _httpClient.Get(url, CancellationToken.None).ConfigureAwait(false))
            {
                info = _jsonSerializer.DeserializeFromStream<Info.VideoList>(site);

                foreach (var i in info.result)
                {
                    var artists = i.artists_main;
                    var featuredArtists = i.artists_featured;
                    var item = new ChannelItemInfo
                    {
                        Name = i.title,
                        ImageUrl = i.image_url,
                        RunTimeTicks = TimeSpan.FromSeconds(i.duration_in_seconds).Ticks,
                        Id = i.isrc,
                        Type = ChannelItemType.Media,
                        ContentType = ChannelMediaContentType.Clip,
                        MediaType = ChannelMediaType.Video,
                        DateCreated = DateTime.Parse(i.created_at)
                        
                    };
                    
                    var overview = "";
                    if (artists.Count == 1)
                        overview = "Artist: " + artists[0].name;
                    else if (artists.Count > 1)
                    {
                        overview = "Artists: ";
                        foreach (var a in artists)
                        {
                            overview = overview + a.name + ", ";
                        }
                        // Strip last ,
                    }
                    
                    if (featuredArtists.Count > 0)
                    {
                        overview = "\nFeaturing: ";
                        foreach (var a in featuredArtists)
                        {
                            overview = overview + a.name + ", ";
                        }
                        // Strip last ,
                    }

                    if (overview != "") item.Overview = overview;

                    items.Add(item);
                }

                if (request == "MostRecent")
                {
                    items = items.OrderByDescending(i => i.DateCreated).ToList();
                }
            }

            return new ChannelItemResult
            {
                Items = items.ToList(),
                TotalRecordCount = items.Count()
            };
        }

       
        public async Task<IEnumerable<ChannelMediaInfo>> GetChannelItemMediaInfo(string id,
            CancellationToken cancellationToken)
        {
            var items = new List<ChannelMediaInfo>();
            Info.VideoNode info;

            using (var site = await _httpClient.Get("http://videoplayer.vevo.com/VideoService/AuthenticateVideo?isrc=" + id, CancellationToken.None).ConfigureAwait(false))
            {
                info = _jsonSerializer.DeserializeFromStream<Info.VideoNode>(site);

                if (info.statusMessage != null)
                {
                    if (!info.statusMessage.ToString().Contains("where you live"))
                    {
                        throw new Exception("You do not live in the right GEO location to play this video.");
                    }
                }

                if (info.video != null)
                {
                    foreach (var v in info.video.videoVersions)
                    {
                        var data = v.data;

                        if (v.sourceType == 4)
                        {
                            var urlNode = Regex.Match(data, "url=\"(.*?)\"", RegexOptions.IgnoreCase);
                            var url = urlNode.Groups[0].Value.Replace(@"\", "").Replace("url=", "").Replace("\"", "");

                            if (url == "") continue;

                            items.Add(new ChannelMediaInfo
                            {
                                Path = url
                            });
                        }
                    }
                }
            }
            return items;
        }


        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            switch (type)
            {
                case ImageType.Thumb:
                case ImageType.Backdrop:
                case ImageType.Primary:
                    {
                        var path = GetType().Namespace + ".Images." + type.ToString().ToLower() + ".png";

                        return Task.FromResult(new DynamicImageResponse
                        {
                            Format = ImageFormat.Png,
                            HasImage = true,
                            Stream = GetType().Assembly.GetManifestResourceStream(path)
                        });
                    }
                default:
                    throw new ArgumentException("Unsupported image type: " + type);
            }
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary
            };
        }

        public string Name
        {
            get { return "Vevo"; }
        }

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Clip
                },
                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Video
                },
                MaxPageSize = 200,
            };
        }

        public string HomePageUrl
        {
            get { return "http://www.vevo.com/"; }
        }

        public ChannelParentalRating ParentalRating
        {
            get { return ChannelParentalRating.GeneralAudience; }
        }
    }
}