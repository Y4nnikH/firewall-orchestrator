#! /usr/bin/perl -w
# iso-importer-main.pl
use strict;
use lib '.';
use CACTUS::FWORCH;
use CACTUS::read_config;
use CGI qw(:standard);          
use Sys::Hostname;

my $isobase	= &CACTUS::read_config::read_config('ITSecOrgDir');
my $importdir	= &CACTUS::read_config::read_config('ImportDir');
my $sleep_time	= &CACTUS::read_config::read_config('ImportSleepTime');
my $hostname_localhost = hostname();
my $importer_hostname = $hostname_localhost;	
my ($res, $mgm_name, $mgm_id, $fehler);

if ($#ARGV>=0) { if (defined($ARGV[0]) && is_numeric($ARGV[0])) { $sleep_time = $ARGV[0] * 1; } }

while (1) {
	output_txt("Import: another loop is starting... ");
	# Managementsysteme aus der DB holen
	my $dbh1 = DBI->connect("dbi:Pg:dbname=$fworch_database;host=$fworch_srv_host;port=$fworch_srv_port","$fworch_srv_user","$fworch_srv_pw");
	if ( !defined $dbh1 ) { die "Cannot connect to database!\n"; }
	my $sth1 = $dbh1->prepare("SELECT mgm_id, mgm_name, do_not_import, importer_hostname from management LEFT JOIN stm_dev_typ USING (dev_typ_id)" .
			" WHERE NOT do_not_import ORDER BY mgm_name" );
	if ( !defined $sth1 ) { die "Cannot prepare statement: $DBI::errstr\n"; }
	$res = $sth1->execute;
	my $management_hash = $sth1->fetchall_hashref('mgm_name');
	$sth1->finish;
	$dbh1->disconnect;
	# Schleife ueber alle Managementsysteme
	foreach $mgm_name (sort keys %{$management_hash}) {
		output_txt("Import: looking at $mgm_name ... ");
		$mgm_id = $management_hash->{"$mgm_name"}->{"mgm_id"};
		if (defined($management_hash->{"$mgm_name"}->{"importer_hostname"})) {
			$importer_hostname = $management_hash->{"$mgm_name"}->{"importer_hostname"};	
		}
		if ($importer_hostname eq $hostname_localhost) {
			output_txt("Import: running on responsible importer $importer_hostname ... ");
			$fehler = system("$importdir/iso-importer-single.pl mgm_id=$mgm_id");
		}
	}
	output_txt("-------- Import module: going back to sleep for $sleep_time seconds --------\n");
	sleep $sleep_time;
}
