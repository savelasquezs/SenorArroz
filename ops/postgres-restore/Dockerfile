FROM postgres:17-alpine

RUN apk add --no-cache aws-cli ca-certificates

COPY restore.sh /restore.sh
RUN chmod +x /restore.sh

CMD ["/restore.sh"]
