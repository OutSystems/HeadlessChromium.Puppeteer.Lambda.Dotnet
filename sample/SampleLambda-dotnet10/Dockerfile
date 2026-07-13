FROM public.ecr.aws/lambda/dotnet:10

# Set the image's internal work directory
WORKDIR /var/task

# Copy function code; keep root ownership, only execution happens as the non-root user
COPY "bin/Release/lambda-publish"  .

# Run as a non-root user (existing "nobody" user in the base image) to reduce blast radius on compromise
USER 65534

# Health check for local/non-Lambda container runtimes; AWS Lambda's own invocation service ignores HEALTHCHECK
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/2015-03-31/functions/function/invocations -d '{}' || exit 1

# Set the CMD to your handler (could also be done as a parameter override outside of the Dockerfile)
CMD [ "SampleLambda::SampleLambda.HelloWorldHandler::Handle" ]