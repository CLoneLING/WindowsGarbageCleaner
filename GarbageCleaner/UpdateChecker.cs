using System;
using System.Threading.Tasks;
using Octokit;
using System.Reflection;
using System.Windows.Forms;

namespace GarbageCleaner
{
    public static class UpdateChecker
    {
        /// <summary>
        /// 检查是否有新版本
        /// </summary>
        /// <param name="owner">仓库所有者，例如 "CLoneLING"</param>
        /// <param name="repo">仓库名称，例如 "WindowsGarbageCleaner"</param>
        /// <returns>如果有新版本则返回 true</returns>
        public static async Task<bool> IsNewVersionAvailable(string owner, string repo)
        {
            try
            {
                // 获取当前运行程序的版本号
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                // 向 GitHub API 发起请求，获取最新的 Release 信息
                var github = new GitHubClient(new ProductHeaderValue("GarbageCleaner-App"));
                var latestRelease = await github.Repository.Release.GetLatest(owner, repo);

                // 解析最新版本的 TagName 并转换为 Version 对象进行比较
                string latestVersionStr = latestRelease.Name.TrimStart('v');
                Version latestVersion = new Version(latestVersionStr);

                // 比较版本：如果最新版本大于当前版本，说明需要更新
                return latestVersion > currentVersion;
            }
            catch (Exception ex)
            {
                // 记录错误，但不中断程序
                Console.WriteLine($"版本检查失败: {ex.Message}");
                return false;
            }
        }
    }
}
