# AWS DDNS on Route 53

> [!WARNING]
> **AI-authored:** This change was autonomously planned and implemented by an AI software factory from a human-authored specification, with possible subsequent human review or modification.

> [!WARNING]
> This experiment is effectively abandoned. The generated material is retained primarily as a research artifact.

Tests a Lambda-backed dynamic DNS service on Route 53: a client posts its hostname and public IP to `POST /hostname` on an API Gateway REST API, the Lambda records the report in DynamoDB and compares the IP with the zone's current A record, and only a differing IP produces a Route 53 `UPSERT`, so repeated identical reports cause no Route 53 mutations.

```sh
make test
make deploy
make e2e
make destroy

HOSTED_ZONE_ID=Z... RECORD_NAME=host.example.com make deploy e2e destroy
```

## Notes

- It's a thing. More an output of the factory than anything.
