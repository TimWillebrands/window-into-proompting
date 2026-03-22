-- Orleans PostgreSQL Initialization Script
-- This script creates the necessary tables and functions for Orleans clustering and grain persistence

-- OrleansQuery table (required for all operations)
CREATE TABLE OrleansQuery
(
    QueryKey varchar(64) NOT NULL,
    QueryText varchar(8000) NOT NULL,
    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);

-- Membership version table
CREATE TABLE OrleansMembershipVersionTable
(
    DeploymentId varchar(150) NOT NULL,
    Timestamp timestamptz(3) NOT NULL DEFAULT now(),
    Version integer NOT NULL DEFAULT 0,
    CONSTRAINT PK_OrleansMembershipVersionTable_DeploymentId PRIMARY KEY(DeploymentId)
);

-- Membership table
CREATE TABLE OrleansMembershipTable
(
    DeploymentId varchar(150) NOT NULL,
    Address varchar(45) NOT NULL,
    Port integer NOT NULL,
    Generation integer NOT NULL,
    SiloName varchar(150) NOT NULL,
    HostName varchar(150) NOT NULL,
    Status integer NOT NULL,
    ProxyPort integer NULL,
    SuspectTimes varchar(8000) NULL,
    StartTime timestamptz(3) NOT NULL,
    IAmAliveTime timestamptz(3) NOT NULL,
    CONSTRAINT PK_MembershipTable_DeploymentId PRIMARY KEY(DeploymentId, Address, Port, Generation),
    CONSTRAINT FK_MembershipTable_MembershipVersionTable_DeploymentId FOREIGN KEY (DeploymentId) REFERENCES OrleansMembershipVersionTable (DeploymentId)
);

-- Orleans Storage table for grain persistence
CREATE TABLE OrleansStorage
(
    grainidhash integer NOT NULL,
    grainidn0 bigint NOT NULL,
    grainidn1 bigint NOT NULL,
    graintypehash integer NOT NULL,
    graintypestring character varying(512) NOT NULL,
    grainidextensionstring character varying(512),
    serviceid character varying(150) NOT NULL,
    payloadbinary bytea,
    modifiedon timestamp without time zone NOT NULL,
    version integer
);

CREATE INDEX ix_orleansstorage ON orleansstorage USING btree (grainidhash, graintypehash);

-- Membership functions
CREATE OR REPLACE FUNCTION update_i_am_alive_time(
    deployment_id varchar,
    address_arg varchar,
    port_arg integer,
    generation_arg integer,
    i_am_alive_time timestamptz)
  RETURNS void AS
$func$
BEGIN
    UPDATE OrleansMembershipTable as d
    SET IAmAliveTime = i_am_alive_time
    WHERE d.DeploymentId = deployment_id AND deployment_id IS NOT NULL
        AND d.Address = address_arg AND address_arg IS NOT NULL
        AND d.Port = port_arg AND port_arg IS NOT NULL
        AND d.Generation = generation_arg AND generation_arg IS NOT NULL;
END
$func$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION insert_membership_version(DeploymentIdArg varchar)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE RowCountVar int := 0;
BEGIN
    INSERT INTO OrleansMembershipVersionTable (DeploymentId)
    SELECT DeploymentIdArg
    ON CONFLICT (DeploymentId) DO NOTHING;
    GET DIAGNOSTICS RowCountVar = ROW_COUNT;
    RETURN QUERY SELECT RowCountVar;
END
$func$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION insert_membership(
    DeploymentIdArg varchar, AddressArg varchar, PortArg integer, GenerationArg integer,
    SiloNameArg varchar, HostNameArg varchar, StatusArg integer, ProxyPortArg integer,
    StartTimeArg timestamptz, IAmAliveTimeArg timestamptz, VersionArg integer)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE RowCountVar int := 0;
BEGIN
    INSERT INTO OrleansMembershipTable
        (DeploymentId, Address, Port, Generation, SiloName, HostName, Status, ProxyPort, StartTime, IAmAliveTime)
    SELECT DeploymentIdArg, AddressArg, PortArg, GenerationArg, SiloNameArg, HostNameArg,
           StatusArg, ProxyPortArg, StartTimeArg, IAmAliveTimeArg
    ON CONFLICT (DeploymentId, Address, Port, Generation) DO NOTHING;

    GET DIAGNOSTICS RowCountVar = ROW_COUNT;

    UPDATE OrleansMembershipVersionTable
    SET Timestamp = now(), Version = Version + 1
    WHERE DeploymentId = DeploymentIdArg AND Version = VersionArg AND RowCountVar > 0;

    RETURN QUERY SELECT RowCountVar;
END
$func$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION update_membership(
    DeploymentIdArg varchar, AddressArg varchar, PortArg integer, GenerationArg integer,
    StatusArg integer, SuspectTimesArg varchar, IAmAliveTimeArg timestamptz, VersionArg integer)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE RowCountVar int := 0;
BEGIN
    UPDATE OrleansMembershipVersionTable
    SET Timestamp = now(), Version = Version + 1
    WHERE DeploymentId = DeploymentIdArg AND Version = VersionArg;

    GET DIAGNOSTICS RowCountVar = ROW_COUNT;

    UPDATE OrleansMembershipTable
    SET Status = StatusArg, SuspectTimes = SuspectTimesArg, IAmAliveTime = IAmAliveTimeArg
    WHERE DeploymentId = DeploymentIdArg AND Address = AddressArg AND Port = PortArg
          AND Generation = GenerationArg AND RowCountVar > 0;

    RETURN QUERY SELECT RowCountVar;
END
$func$ LANGUAGE plpgsql;

-- Storage functions
CREATE OR REPLACE FUNCTION writetostorage(
    _grainidhash integer, _grainidn0 bigint, _grainidn1 bigint,
    _graintypehash integer, _graintypestring character varying,
    _grainidextensionstring character varying, _serviceid character varying,
    _grainstateversion integer, _payloadbinary bytea)
  RETURNS TABLE(newgrainstateversion integer) LANGUAGE 'plpgsql' AS $function$
DECLARE
    _newGrainStateVersion integer := _GrainStateVersion;
    RowCountVar integer := 0;
BEGIN
    IF _GrainStateVersion IS NOT NULL THEN
        UPDATE OrleansStorage
        SET PayloadBinary = _PayloadBinary, ModifiedOn = (now() at time zone 'utc'), Version = Version + 1
        WHERE GrainIdHash = _GrainIdHash AND GrainTypeHash = _GrainTypeHash
              AND GrainIdN0 = _GrainIdN0 AND GrainIdN1 = _GrainIdN1
              AND GrainTypeString = _GrainTypeString
              AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
              AND ServiceId = _ServiceId AND Version = _GrainStateVersion;
        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0 THEN _newGrainStateVersion := _GrainStateVersion + 1; END IF;
    END IF;

    IF _GrainStateVersion IS NULL THEN
        INSERT INTO OrleansStorage
            (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString,
             GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version)
        SELECT _GrainIdHash, _GrainIdN0, _GrainIdN1, _GrainTypeHash, _GrainTypeString,
               _GrainIdExtensionString, _ServiceId, _PayloadBinary, (now() at time zone 'utc'), 1
        WHERE NOT EXISTS (
            SELECT 1 FROM OrleansStorage WHERE GrainIdHash = _GrainIdHash AND GrainTypeHash = _GrainTypeHash
                  AND GrainIdN0 = _GrainIdN0 AND GrainIdN1 = _GrainIdN1 AND GrainTypeString = _GrainTypeString
                  AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                  AND ServiceId = _ServiceId
        );
        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0 THEN _newGrainStateVersion := 1; END IF;
    END IF;
    RETURN QUERY SELECT _newGrainStateVersion;
END $function$;

-- Insert Orleans queries
INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('UpdateIAmAlivetimeKey', 'SELECT * from update_i_am_alive_time(@DeploymentId, @Address, @Port, @Generation, @IAmAliveTime)');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('InsertMembershipVersionKey', 'SELECT * FROM insert_membership_version(@DeploymentId)');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('InsertMembershipKey', 'SELECT * FROM insert_membership(@DeploymentId, @Address, @Port, @Generation, @SiloName, @HostName, @Status, @ProxyPort, @StartTime, @IAmAliveTime, @Version)');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('UpdateMembershipKey', 'SELECT * FROM update_membership(@DeploymentId, @Address, @Port, @Generation, @Status, @SuspectTimes, @IAmAliveTime, @Version)');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('MembershipReadRowKey', 'SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName, m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version FROM OrleansMembershipVersionTable v LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId AND Address = @Address AND Port = @Port AND Generation = @Generation WHERE v.DeploymentId = @DeploymentId');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('MembershipReadAllKey', 'SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName, m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version FROM OrleansMembershipVersionTable v LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId WHERE v.DeploymentId = @DeploymentId');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('DeleteMembershipTableEntriesKey', 'DELETE FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId; DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('GatewaysQueryKey', 'SELECT Address, ProxyPort, Generation FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId AND Status = @Status AND ProxyPort > 0');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('WriteToStorageKey', 'SELECT * FROM WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary)');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('ReadFromStorageKey', 'SELECT PayloadBinary, (now() at time zone ''utc''), Version FROM OrleansStorage WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL) AND ServiceId = @ServiceId');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('ClearStorageKey', 'UPDATE OrleansStorage SET PayloadBinary = NULL, Version = Version + 1 WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL) AND ServiceId = @ServiceId AND Version = @GrainStateVersion RETURNING Version');

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('DeleteStorageKey', 'DELETE FROM OrleansStorage WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL) AND ServiceId = @ServiceId AND Version = @GrainStateVersion RETURNING Version + 1');
