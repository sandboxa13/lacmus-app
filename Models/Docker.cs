﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Newtonsoft.Json;

namespace RescuerLaApp.Models
{
    public class Docker : IDisposable
    {
        private readonly DockerClient _client;
        private const int API_VER = 1;
        private const bool IS_GPU = false;

        public Docker()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _client = new DockerClientConfiguration(
                        new Uri("npipe://./pipe/docker_engine"))
                    .CreateClient();
            }
            else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _client = new DockerClientConfiguration(
                        new Uri("unix:///var/run/docker.sock"))
                    .CreateClient();
            }
            else
            {
                throw new Exception($"Your system dose not supported: {RuntimeInformation.OSDescription}");
            }
        }
        
        public async Task Initialize(string imageName = "gosha20777/test", string tag = "1")
        {
            tag = IS_GPU ? $"gpu-{API_VER}.{tag}" : $"{API_VER}.{tag}";
            try
            {
                var progressDictionary = new Dictionary<string, string>();
                var images = await _client.Images.ListImagesAsync(new ImagesListParameters {MatchName = $"{imageName}:{tag}"});
                if (images.Count > 0)
                {
                    Console.WriteLine($"such image already exists: {images.First().ID}");
                    return;
                }
                
                var progress = new Progress<JSONMessage>();
                progress.ProgressChanged += (sender, message) =>
                {
                    try
                    {
                        if (progressDictionary.ContainsKey(message.ID) && message.Status.Contains("Pull complete",
                                    StringComparison.InvariantCultureIgnoreCase))
                        {
                            progressDictionary[message.ID] = message.Status;
                            var count = progressDictionary.Cast<string>().Count(value =>
                                    value.Contains("Pull complete", StringComparison.InvariantCultureIgnoreCase));
                            Debug.Assert(progressDictionary.Count > 1, "progressDictionary.Count > 1");
                            Console.WriteLine($"{count}/{progressDictionary.Count - 1}");
                        }
                        else
                            progressDictionary.Add(message.ID, message.Status);
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                };


                await _client.Images.CreateImageAsync(
                    new ImagesCreateParameters
                    {
                        FromImage = $"{imageName}:{tag}"
                    },
                    new AuthConfig
                    {
                        Email = "lizaalertai@yandex.ru",
                        Username = "lizaalertai",
                        Password = "9ny?Mh4b*qfThZ6T"
                    },
                    progress
                );
            }
            catch (Exception e)
            {
                throw new Exception($"Unable to create docker image: {e.Message}");
            }
        }

        public async Task<string> CreateContainer(string imageName = "gosha20777/test", string tag = "1")
        {
            //TODO здесь бы все это из файла конфигурировать
            tag = IS_GPU ? $"gpu-{API_VER}.{tag}" : $"{API_VER}.{tag}";
            try
            {
                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters {All = true});
                foreach (var container in containers)
                {
                    if (container.Image == $"{imageName}:{tag}")
                    {
                        Console.WriteLine($"such container already exists: {container.ID} {container.Image}");
                        return container.ID;
                    }
                }

                if (IS_GPU)
                {
                    var stdOut = "";
                    var bash = new BashCommand();
                    stdOut = bash.Execute($"docker create --runtime=nvidia -p 5000:5000 {imageName}:{tag}", out var err);
                    
                    await Task.Delay(800);
                    stdOut = stdOut.Replace(Environment.NewLine, String.Empty);
                    if(string.IsNullOrWhiteSpace(stdOut) || !string.IsNullOrWhiteSpace(err))
                        throw new Exception($"invalid id {err}");

                    return stdOut;
                }

                //else
                var containerCreateResponse = await _client.Containers.CreateContainerAsync(
                    new CreateContainerParameters
                    {
                        Image = $"{imageName}:{tag}",
                        HostConfig = new HostConfig
                        {
                            PortBindings = new Dictionary<string, IList<PortBinding>>
                            {
                                { "5000", new List<PortBinding> { new PortBinding { HostPort = "5000" } } }
                            }
                        },
                        ExposedPorts = new Dictionary<string, EmptyStruct>
                        {
                            { "5000", new EmptyStruct() }
                        }
                    });
                if (string.IsNullOrWhiteSpace(containerCreateResponse.ID))
                {
                    throw new Exception("invalid id");
                }

                return containerCreateResponse.ID;
            }
            catch (Exception e)
            {
                throw new Exception($"Unable to create docker container: {e.Message}");
            }
        }

        public async Task<bool> Run(string id)
        {
            
            try
            {
                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters {All = true});
                foreach (var container in containers)
                {
                    if (container.ID == id && container.Status.Contains("Up"))
                    {
                        Console.WriteLine($"such container already running: {container.ID} {container.Image} {container.Status}");
                        return true;
                    }
                }
                
                return await _client.Containers.StartContainerAsync(id, new ContainerStartParameters());
            }
            catch (Exception e)
            {
                throw new Exception($"Unable to start docker container: {e.Message}");
            }
        }

        public async Task StopAll(string imageName = "gosha20777/test")
        {
            try
            {
                var containers =
                        await _client.Containers.ListContainersAsync(new ContainersListParameters {All = true});
                foreach (var container in containers)
                {
                    if (container.Image.Contains(imageName) && container.Status.Contains("Up"))
                    {
                        var success =
                                await _client.Containers.StopContainerAsync(container.ID,
                                        new ContainerStopParameters());
                        Console.Write($"stopping container: {container.ID} {container.Image} {success.ToString()}");
                        return;
                    }
                }
            }
            catch(Exception e)
            {
                throw new Exception($"Unable to stop docker container: {e.Message}");
            }
        }
        
        public async Task Stop(string id)
        {
            try
            {
                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters {All = true});
                foreach (var container in containers)
                {
                    if (container.ID == id && container.Status.Contains("Exited"))
                    {
                        Console.WriteLine($"such container already stopped: {container.ID} {container.Image} {container.Status}");
                        return;
                    }
                }
                
                var success = await _client.Containers.StopContainerAsync(id, new ContainerStopParameters());
                if(!success)
                    throw new Exception("returns false");
            }
            catch (Exception e)
            {
                throw new Exception($"Unable to stop docker container: {e.Message}");
            }
        }

        public async Task<List<string>> GetTags(string imageName = "gosha20777/test")
        {
            var baseUrl = "https://registry.hub.docker.com";
            try
            {
                var client = new RestApiClient(baseUrl);
                var result = new List<string>();
                var response = new DockerTagResponse { Next = baseUrl + $"/v2/repositories/{imageName}/tags/"};
            
                while (!string.IsNullOrEmpty(response.Next) && response.Next.Contains(baseUrl))
                {
                    var jsonResp = await client.GetAsync(response.Next.Remove(0, baseUrl.Length));
                    response = JsonConvert.DeserializeObject<DockerTagResponse>(jsonResp);
                    result.AddRange(response.Images.Select(image => image.Tag).ToList());
                }
            
                var resTags = new List<string>();
                foreach (var r in result)
                {
                    var prefix = "";
                    if (IS_GPU)
                    {
                        prefix = $"gpu-{API_VER}.";
                        if(r.Contains(prefix))
                            resTags.Add(r.Replace(prefix, ""));
                    }
                    else
                    {
                        prefix = $"{API_VER}.";
                        if(!r.Contains("gpu") && r.Contains(prefix))
                            resTags.Add(r.Replace(prefix, ""));
                    }
                }
                return resTags;
            }
            catch(Exception e)
            {
                throw new Exception($"Unable to retrieve tag(s): {e.Message}");
            }
        }

        public async Task Remove(string imageName = "gosha20777/test", string tag = "1")
        {
            tag = IS_GPU ? $"gpu-{API_VER}.{tag}" : $"{API_VER}.{tag}";
            try
            {
                //stop and remove all containers
                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters {All = true});
                foreach (var container in containers)
                {
                    if (container.Image == $"{imageName}:{tag}")
                    {
                        await Stop(container.ID);
                        await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters {Force = true});
                    }
                }
                
                var images = await _client.Images.ListImagesAsync(new ImagesListParameters {MatchName = $"{imageName}:{tag}"});
                foreach (var image in images)
                {
                    await _client.Images.DeleteImageAsync(image.ID, new ImageDeleteParameters {Force = true});
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Unable to create docker image: {e.Message}");
            }
        }

        public async Task<List<string>> GetInstalledVersions(string imageName = "gosha20777/test")
        {
            try
            {
                var tags = new List<string>();
                var resTags = new List<string>();
                var images = await _client.Images.ListImagesAsync(new ImagesListParameters {MatchName = imageName});
                foreach (var image in images)
                {
                    tags.AddRange(image.RepoTags);
                }

                foreach (var tag in tags)
                {
                    var prefix = "";
                    if (IS_GPU)
                    {
                        prefix = $"gpu-{API_VER}.";
                        if(tag.Contains($"{imageName}:{prefix}"))
                            resTags.Add(tag.Replace($"{imageName}:{prefix}", ""));
                    }
                    else
                    {
                        prefix = $"{API_VER}.";
                        if(!tag.Contains("gpu") && tag.Contains(prefix))
                            resTags.Add(tag.Replace($"{imageName}:{prefix}", ""));
                    }
                }
                return resTags;
            }
            catch(Exception e)
            {
                throw new Exception($"Unable to retrieve installed versions: {e.Message}");
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
        
        [JsonObject]
        private class DockerTagResponse
        {
            [JsonProperty("count")]
            public int Count { get; set; }
            [JsonProperty("next")]
            public string Next { get; set; }
            [JsonProperty("results")]
            public DockerImageResponse[] Images { get; set; }
        }
        
        [JsonObject]
        private class DockerImageResponse
        {
            [JsonProperty("name")]
            public string Tag { get; set; }
        }
    }
}