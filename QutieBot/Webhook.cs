using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QutieBot.Bot;
using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QutieBot
{
    /// <summary>
    /// Background service that listens for webhook requests to create interview rooms
    /// </summary>
    public class Webhook : BackgroundService
    {
        private readonly HttpListener _httpListener;
        private readonly InterviewRoom _interviewRoom;
        private readonly ILogger<Webhook> _logger;
        private readonly string _webhookUrl = "http://+:5000/webhook/";

        /// <summary>
        /// Initializes a new instance of the Webhook class
        /// </summary>
        /// <param name="interviewRoom">The interview room service</param>
        /// <param name="logger">The logger instance</param>
        public Webhook(
            InterviewRoom interviewRoom,
            ILogger<Webhook> logger)
        {
            _interviewRoom = interviewRoom ?? throw new ArgumentNullException(nameof(interviewRoom));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(_webhookUrl);
        }

        /// <summary>
        /// Executes the background service
        /// </summary>
        /// <param name="stoppingToken">Cancellation token to stop the service</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _httpListener.Start();
                _logger.LogInformation($"Webhook listener started on {_webhookUrl}");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var context = await _httpListener.GetContextAsync();

                    try
                    {
                        await ProcessRequestAsync(context, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing webhook request");

                        try
                        {
                            // Send error response if we haven't sent one yet
                            if (context.Response.StatusCode == (int)HttpStatusCode.OK)
                            {
                                await SendErrorResponseAsync(
                                    context,
                                    HttpStatusCode.InternalServerError,
                                    "An error occurred while processing the request",
                                    stoppingToken);
                            }
                        }
                        catch (Exception responseEx)
                        {
                            _logger.LogError(responseEx, "Error sending error response");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Webhook service was canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in webhook listener");
            }
            finally
            {
                if (_httpListener.IsListening)
                {
                    _httpListener.Stop();
                    _logger.LogInformation("Webhook listener stopped");
                }
            }
        }

        /// <summary>
        /// Processes an incoming HTTP request
        /// </summary>
        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken stoppingToken)
        {
            var request = context.Request;
            var remoteEndpoint = request.RemoteEndPoint;

            _logger.LogDebug($"Received {request.HttpMethod} request from {remoteEndpoint?.Address}:{remoteEndpoint?.Port}");

            if (request.HttpMethod != "GET")
            {
                _logger.LogWarning($"Rejected {request.HttpMethod} request: only GET is supported");
                await SendErrorResponseAsync(
                    context,
                    HttpStatusCode.MethodNotAllowed,
                    "Only GET requests are allowed",
                    stoppingToken);
                return;
            }

            var query = request.QueryString;
            var userId = query["userId"];
            var submissionId = query["submissionId"];

            _logger.LogDebug($"Request parameters: userId={userId}, submissionId={submissionId}");

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(submissionId))
            {
                _logger.LogWarning("Missing required parameters");
                await SendErrorResponseAsync(
                    context,
                    HttpStatusCode.BadRequest,
                    "Both userId and submissionId are required",
                    stoppingToken);
                return;
            }

            // Validate parameters can be parsed
            if (!ulong.TryParse(userId, out ulong parsedUserId))
            {
                _logger.LogWarning($"Invalid userId format: {userId}");
                await SendErrorResponseAsync(
                    context,
                    HttpStatusCode.BadRequest,
                    "userId must be a valid numeric value",
                    stoppingToken);
                return;
            }

            if (!long.TryParse(submissionId, out long parsedSubmissionId))
            {
                _logger.LogWarning($"Invalid submissionId format: {submissionId}");
                await SendErrorResponseAsync(
                    context,
                    HttpStatusCode.BadRequest,
                    "submissionId must be a valid numeric value",
                    stoppingToken);
                return;
            }

            // Create the interview room
            try
            {
                _logger.LogInformation($"Creating interview room for user {parsedUserId} with submission {parsedSubmissionId}");
                await _interviewRoom.CreateInterviewRoomAsync(parsedUserId, parsedSubmissionId);

                // Send success response
                await SendSuccessResponseAsync(context, parsedUserId, stoppingToken);
                _logger.LogInformation($"Successfully created interview room for user {parsedUserId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create interview room for user {parsedUserId}");
                throw; // Will be caught by the outer try/catch
            }
        }

        /// <summary>
        /// Sends a success response
        /// </summary>
        private async Task SendSuccessResponseAsync(HttpListenerContext context, ulong userId, CancellationToken cancellationToken)
        {
            var responseObj = new { success = true, receivedUserId = userId.ToString() };
            await SendJsonResponseAsync(context, HttpStatusCode.OK, responseObj, cancellationToken);
        }

        /// <summary>
        /// Sends an error response
        /// </summary>
        private async Task SendErrorResponseAsync(
            HttpListenerContext context,
            HttpStatusCode statusCode,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            var responseObj = new { success = false, error = errorMessage };
            await SendJsonResponseAsync(context, statusCode, responseObj, cancellationToken);
        }

        /// <summary>
        /// Sends a JSON response
        /// </summary>
        private async Task SendJsonResponseAsync(
            HttpListenerContext context,
            HttpStatusCode statusCode,
            object responseObj,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = context.Response;
                response.StatusCode = (int)statusCode;
                response.ContentType = "application/json";

                var responseJson = JsonSerializer.Serialize(responseObj);
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);

                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            }
            finally
            {
                context.Response.Close();
            }
        }

        /// <summary>
        /// Stops the webhook service
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping webhook service");

            if (_httpListener.IsListening)
            {
                _httpListener.Stop();
                _logger.LogInformation("Webhook listener stopped");
            }

            await base.StopAsync(cancellationToken);
        }
    }
}