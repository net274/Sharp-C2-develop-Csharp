using System.Threading.Tasks;

using AutoMapper;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SharpC2.API;
using SharpC2.API.V1.Requests;
using SharpC2.API.V1.Responses;

using TeamServer.Interfaces;
using TeamServer.Models;

namespace TeamServer.Controllers
{
    [ApiController]
    [Authorize]
    [Route(Routes.V1.Payloads)]
    public class PayloadsController : ControllerBase
    {
        private readonly IMapper _mapper;
        private readonly IHandlerService _handlers;

        public PayloadsController(IMapper mapper, IHandlerService handlers)
        {
            _mapper = mapper;
            _handlers = handlers;
        }

        [HttpPost]
        public async Task<IActionResult> GeneratePayload([FromBody] PayloadRequest request)
        {
            var handler = _handlers.GetHandler(request.Handler);
            if (handler is null) return NotFound();
            
            var payload = _mapper.Map<PayloadRequest, Payload>(request);
            payload.SetHandler(handler);
            await payload.Generate();

            var response = _mapper.Map<Payload, PayloadResponse>(payload);
            return Ok(response);
        }
    }
}