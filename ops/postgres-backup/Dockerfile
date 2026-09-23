FROM postgres:17-alpine

RUN apk add --no-cache aws-cli ca-certificates coreutils

COPY backup.sh /backup.sh
RUN chmod +x /backup.sh

CMD ["/backup.sh"]
